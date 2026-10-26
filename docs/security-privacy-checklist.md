# Security and privacy checklist

## Implemented in v1

| Area | Control |
|------|---------|
| Transport | HTTPS termination at the platform (Coolify/Traefik); API never terminates TLS itself |
| Passwords | ASP.NET Core Identity hashing (PBKDF2), configurable policy (8+, upper/lower/digit) |
| Brute force | Identity lockout (5 fails → 15 min) + per-IP fixed window on auth endpoints (20/min) + global sliding window (300/min) |
| Sessions | Short-lived JWT (15 min) + rotating hashed refresh tokens; reuse → revoke-all |
| SQL | 100% parameterised (Dapper `@params`); list values expanded into per-item parameters — no string interpolation of values |
| AuthZ | Resource-level ownership checks (cancel/report/read) + role policies for admin endpoints |
| Location | Generalized to ~1 km before storage (ADR-0005); never returned to other users; deleted on consent withdrawal |
| Push payloads | No tokens, medical data, phone numbers, precise locations |
| Audit | Append-only `audit_log` + `donation_record_events` (DB triggers reject UPDATE/DELETE) |
| Abuse v1 | Reporting, moderator review, suspension (revokes sessions), duplicate-response protection, request caps + expiry |
| Errors | ProblemDetails; internal exceptions never leaked; structured logs with correlation ids |
| Secrets | Via environment only; `.env` git-ignored; JWT key required at startup |
| Data minimisation | No medical history, no identity documents, no patient records collected |
| Migrations | Least-privilege guidance; append-only enforced at the DB, not by role |

## Not loggable (and not logged)

Credentials, tokens, precise donor coordinates, patient information. The
dev verification-code sender logs codes **by design for development only**
and is replaced by a real provider in production.

## Regulatory questions AF Foundation must resolve before launch

- Applicable Pakistani data-protection legislation and any provincial
  requirements for health-adjacent data; a legal review of this checklist.
- The privacy policy on the site is a **placeholder** documenting actual
  behaviour — it needs legal wording and a named responsible contact.
- Retention windows for audit logs and donation records (currently kept
  indefinitely; configurable deletion procedures are a documented v2 item).
- OneSignal as a processor: review its DPA/privacy terms.
- Terms of use: medical-disclaimer wording reviewed by counsel.

## Operational hardening before public launch

- [ ] Replace the dev JWT key and DB password with generated secrets
- [ ] Dedicated non-superuser DB account for the API/Worker
- [ ] Remove `ADMIN_BOOTSTRAP_KEY` after creating the first admin
- [ ] Activate a medically approved deferral policy version
- [ ] Configure a real email/SMS provider for verification codes
- [ ] Enable off-host backups + first restore drill
- [ ] Set CORS to the exact production origin(s)
- [ ] Review logs for accidental PII; add log scrubbing if needed
- [ ] Dependency vulnerability scan in CI (`dotnet list package --vulnerable`, `npm audit`)
