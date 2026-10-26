# Local development

## 1. Database (Docker)

```bash
cp .env.example .env          # local defaults; edit if you change passwords
docker compose up -d postgres
docker compose run --rm flyway migrate
```

- PostgreSQL 17 + PostGIS listens on `127.0.0.1:5433` (avoid clashing with a
  local 5432).
- Connection string used by API/Worker defaults:
  `Host=127.0.0.1;Port=5433;Database=aff_blood;Username=aff;Password=aff_dev_password`
- Flyway runs as a one-shot job; re-running it applies only new migrations
  (`flyway_schema_history` tracks state).
- Reset everything: `docker compose down -v` (destroys local data).

## 2. API

```bash
dotnet run --project src/AF.Blood.Api
```

- Development environment loads `appsettings.Development.json`, which contains
  a **well-known insecure** JWT signing key and the bootstrap key
  `dev-bootstrap-key`. Never reuse them outside local development.
- Endpoints: `http://127.0.0.1:8080/api/v1/...`
- OpenAPI document: `http://127.0.0.1:8080/openapi/v1.json` (dev only) — paste
  it into Swagger UI / Scalar / Insomnia. Bearer auth via the padlock.

## 3. Worker

```bash
dotnet run --project src/AF.Blood.Worker
```

Jobs: matching-run processing (10 s), outbox delivery (5 s), request expiry +
housekeeping (30 s). Console logs each cycle. Without OneSignal credentials
the push dispatcher is a no-op logger — in-app notification history still
works, and nothing is ever *claimed* as delivered.

## 4. Verification codes (email/phone)

The dev `IVerificationCodeSender` logs codes to the console:

```
DEV-ONLY verification code for EmailVerification to user@example.test: 123456.
```

Use them at `POST /auth/verification/confirm` and `POST /auth/password/reset`.

## 5. Landing site

```bash
cd web/af-foundation-site
npm ci
npm run dev          # http://localhost:5173
npm run build        # production bundle in dist/
npm run check        # type-check
```

## 6. Mobile app

```bash
cd mobile/af_blood_app
flutter pub get
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:8080/api/v1   # Android emulator
# iOS simulator: --dart-define=API_BASE_URL=http://127.0.0.1:8080/api/v1
```

See [flutter-build.md](flutter-build.md) for OneSignal/deep-link setup.

## 7. Tests

```bash
dotnet test tests/AF.Blood.UnitTests          # fast, no Docker needed
dotnet test tests/AF.Blood.IntegrationTests   # starts a Postgres 17/PostGIS container
```

Integration tests migrate a template database using the exact SQL in
`database/sql` and clone one fresh database per test
([ADR-0008](adr/0008-testing-strategy.md)).

## 8. Full stack in Docker

```bash
docker compose up --build
# postgres :5433, api :8080, worker (no port), web :4173
```

## Common tasks

**Create the first admin** — see README ("First administrator").

**Change wave radii** — `Matching:RadiiKm` (comma-separated) for API and
Worker; existing scheduled runs keep their recorded radius.

**Point at another database** — override `ConnectionStrings__Default`
(environment) or edit `appsettings.Development.json`.

**Reset a user's password as admin** — use the verification-code flow or
update through `POST /auth/password/forgot` + code from the dev log.
