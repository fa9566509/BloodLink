# API reference

## Conventions

- Prefix: `/api/v1`. JSON bodies. Errors are **RFC 7807 Problem Details**
  (`application/problem+json`) with an `errorCode` extension; internal
  exception messages are never returned.
- Auth: `Authorization: Bearer <access token>` (JWT, 15 min).
- Idempotency/duplication: storage-level unique constraints make retried
  writes safe (one response per donor per request, one non-rejected donation
  per request, unique refresh-token hashes, unique OneSignal subscription
  per device row).
- Rate limits: global sliding window (300 req/min per user or IP) plus a
  stricter fixed window (20/min per IP) on authentication endpoints.
- Correlation: every response carries `X-Correlation-Id` (echo or generated).

## OpenAPI

In development the document is served at
`http://127.0.0.1:8080/openapi/v1.json` (`AddOpenApi`). Import into Swagger
UI/Insomnia/Postman; bearer auth is declared via the `Bearer` scheme.

## Endpoint map

### Auth — `AuthEndpoints`
- `POST /auth/register` · `POST /auth/login` · `POST /auth/refresh` ·
  `POST /auth/logout` · `GET /auth/me`
- `POST /auth/verification/request` · `POST /auth/verification/confirm`
- `POST /auth/password/forgot` · `POST /auth/password/reset` ·
  `POST /auth/password/change`

### Donors — `DonorEndpoints` (all authenticated)
- `PUT /donors/me` — create/update blood group + radius
- `GET /donors/me`
- `PUT /donors/me/consent` — grant/withdraw discovery consent (+approx location)
- `PUT /donors/me/availability`
- `GET /donors/me/eligibility` — deferral countdown + mandated wording

### Requests — `RequestEndpoints`
- `POST /requests` — create whole-blood request (schedules wave 0)
- `GET /requests/nearby` — open requests the caller's blood group can help
- `GET /requests/mine` · `GET /responses/mine`
- `GET /requests/{id}` · `GET /requests/{id}/responses` (moderator+)
- `POST /requests/{id}/responses` — `{"response": "accepted"|"declined"}`
- `POST /requests/{id}/cancel` (owner)
- `POST /requests/{id}/donation-report` (matched donor)
- `POST /requests/{id}/donation-acknowledgement` (owner: confirm/dispute)
- `GET /donations/mine` · `GET /donations/{id}` (owner/admin)

### Notifications & devices — `NotificationEndpoints`
- `GET /notifications` · `GET /notifications/unread-count`
- `POST /notifications/mark-read`
- `POST /devices` · `GET /devices` · `POST /devices/{id}/disable`
- `POST /reports/abuse`

### Admin — `AdminEndpoints`
- `POST /admin/bootstrap` — first-admin promotion (bootstrap key)
- `GET /admin/abuse-reports?status=` · `POST /admin/abuse-reports/{id}/resolve`
- `POST /admin/users/{id}/suspend` · `POST /admin/users/{id}/restore`
- `GET /admin/donations?status=` (default: reported/disputed)
- `POST /admin/donations/{id}/verify|reject` · `POST /admin/donations/{id}/correct`
- `GET /admin/policies` · `POST /admin/policies` ·
  `POST /admin/policies/{id}/activate`

Roles: `moderator` (abuse, suspensions), `donation_verifier` (+verify/reject),
`administrator` (all, incl. corrections and policies).

## Status codes worth knowing

| Situation | Code |
|-----------|------|
| Concurrent accept lost the slot / duplicate response / already matched | 409 `conflict` |
| Suspended account (login/refresh/sensitive action) | 403 `forbidden` |
| Missing consent/availability on accept | 409 with explanatory detail |
| Weak password / validation | 400 with the first Identity error |
| Refresh token reuse detected | 401 (403 if suspended) and all sessions revoked |
