# ADR-0002 — Authentication: Identity + JWT access tokens + rotating refresh sessions

Date: 2026-10-26 · Status: Accepted

## Decision

- ASP.NET Core Identity (EF Core stores) owns users, password hashing,
  lockout, and security stamps.
- API access uses **short-lived JWT bearer access tokens** (15 min, HS256,
  key ≥ 32 chars from configuration).
- Sessions are **rotating refresh tokens** persisted hashed (SHA-256) in
  `auth_refresh_tokens`: 30-day expiry, rotation on every refresh, reuse of a
  rotated token revokes all of the user's sessions (theft detection).
- `POST /auth/logout` revokes the presented session. Password reset revokes
  all sessions. Access tokens intentionally remain valid up to 15 minutes
  after revocation — documented tradeoff for stateless verification; sensitive
  v1 actions re-check account suspension server-side.
- Email/phone verification and password recovery use 6-digit codes stored
  hashed in `verification_codes` (10-min TTL, max 5 attempts). Delivery is via
  an `IVerificationCodeSender` port: the dev implementation logs the code; a
  production SMS/email provider must be configured before public launch
  (tracked in the launch checklist).

## Why not cookies / OIDC server

Flutter clients on Android/iOS handle bearer tokens more predictably than
cookie jars, and Identity alone does not provide a full OAuth/OIDC
authorization server. A dedicated OIDC server (e.g. Duende/Keycloak) is
out of proportion for v1's single first-party client; revisit if third-party
clients arrive.
