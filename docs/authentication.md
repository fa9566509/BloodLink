# Authentication design (ADR-0002)

## Components

- **ASP.NET Core Identity** (EF Core stores, UUID keys, Flyway-owned schema):
  users, password hashing (PBKDF2), lockout (5 failed logins → 15 min),
  normalized emails, security stamps, roles.
- **Access tokens**: JWT, HS256, 15-minute lifetime, claims `sub`, `email`,
  `name`, `jti`, and one `role` claim per role. Key ≥ 32 chars from
  configuration (`Jwt:Key`) — the API refuses to start without one.
- **Refresh sessions**: opaque 256-bit tokens, stored **hashed** (SHA-256) in
  `auth_refresh_tokens`. 30-day expiry, rotation on every refresh, and reuse
  detection: presenting a token that was already rotated revokes **all** of
  that user's sessions (theft response).

## Endpoints

| Endpoint | Auth | Notes |
|----------|------|-------|
| `POST /auth/register` | – | Identity password policy; returns token pair |
| `POST /auth/login` | – | Lockout enforced; suspended accounts get 403 |
| `POST /auth/refresh` | – | Rotates; reuse → revoke-all (+403 if suspended) |
| `POST /auth/logout` | bearer | Revokes the presented session |
| `GET /auth/me` | bearer | Current user incl. roles, verification flags |
| `POST /auth/verification/request` / `confirm` | bearer | 6-digit codes, hashed at rest, 10-min TTL, 5 attempts |
| `POST /auth/password/forgot` | – | Always the same response (no account enumeration) |
| `POST /auth/password/reset` | – | Consumes the code, revokes all sessions |
| `POST /auth/password/change` | bearer | Revokes all sessions |

## Verification / recovery code delivery

Codes go through the `IVerificationCodeSender` port. Development logs them.
**Production must configure a real email/SMS provider before public launch**
— see [launch configuration](launch-configuration.md). Codes are stored
hashed in `verification_codes` with attempt counters.

## Session semantics & tradeoffs

- Logout revokes the refresh session; the access token remains valid for up
  to 15 minutes (stateless verification). Sensitive server-side flows
  re-check suspension; the blast radius of a stolen access token is the
  short remaining lifetime.
- Password change/reset revokes every session immediately.
- Suspension revokes every refresh session and blocks login/refresh with 403.
- Refresh rotation is atomic; concurrent refreshes with the same token are
  serialised by the row lock and the loser is treated as reuse.

## Roles

| Role | Grants |
|------|--------|
| *(implicit member)* | All donor/request endpoints |
| `moderator` | Abuse reports, suspensions |
| `donation_verifier` | Donation verify/reject (plus moderator endpoints) |
| `administrator` | Everything, incl. policy management and corrections |

Role claims are baked into access tokens; after a role change (e.g.
bootstrap), the client must re-login to obtain the new claim.

## Flutter client behaviour

- Tokens in `flutter_secure_storage` (Keystore/Keychain-backed).
- dio interceptor adds the bearer token; a single transparent refresh+replay
  occurs on 401; a failed refresh emits a session-expired event that returns
  the user to the welcome screen.
