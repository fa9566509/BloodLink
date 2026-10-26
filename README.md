# AF Foundation — Blood Donation Coordination Platform

A modular monolith (.NET 10 + PostgreSQL/PostGIS + Flyway) that helps people in
Pakistan find nearby blood donors: donors consent to discovery, requesters
create whole-blood requests, eligible nearby donors are notified in waves, and
donation outcomes are reported with provenance-preserving verification and
policy-backed deferrals.

| Component      | Path                            | Stack |
|----------------|---------------------------------|-------|
| API            | `src/AF.Blood.Api`              | ASP.NET Core (.NET 10), JWT + Identity |
| Worker         | `src/AF.Blood.Worker`           | .NET Worker (matching, outbox, expiry) |
| Database       | `database/sql`                  | PostgreSQL 17 + PostGIS, Flyway migrations |
| Mobile app     | `mobile/af_blood_app`           | Flutter (Android/iOS), Riverpod, go_router |
| Landing site   | `web/af-foundation-site`        | TanStack Router + React + Vite (static) |
| Deployment     | `docker-compose.yml`, `deploy/` | Docker Compose (dev), Coolify (prod) |

Full documentation lives in [`docs/`](docs) — start with
[architecture](docs/architecture.md) and [local development](docs/local-development.md).

## Prerequisites

- .NET SDK 10 (the app targets `net10.0`)
- Docker Desktop (PostgreSQL/PostGIS + Flyway run in containers)
- Node 20+ (landing site)
- Flutter SDK (mobile app; Android SDK for Android builds)

## Quick start (local development)

```bash
# 1. Environment (local defaults; edit .env if needed)
cp .env.example .env

# 2. Start PostgreSQL + apply migrations (Flyway runs as a one-shot job)
docker compose up -d postgres
docker compose run --rm flyway migrate

# 3. Run the API  (http://127.0.0.1:8080, OpenAPI at /openapi/v1.json in dev)
dotnet run --project src/AF.Blood.Api

# 4. Run the background worker (matching waves, outbox delivery, expiry)
dotnet run --project src/AF.Blood.Worker

# 5. Landing site (optional)
cd web/af-foundation-site && npm ci && npm run dev

# 6. Mobile app (optional; Android emulator default points at 10.0.2.2:8080)
cd mobile/af_blood_app && flutter pub get && flutter run \
  --dart-define=API_BASE_URL=http://10.0.2.2:8080/api/v1
```

Everything also runs as one stack with `docker compose up --build` (postgres +
flyway + api + worker + web).

## First administrator

Register a normal account, then — while `Admin:BootstrapKey` is set (dev
default: `dev-bootstrap-key`) and no administrator exists yet:

```bash
curl -X POST http://127.0.0.1:8080/api/v1/admin/bootstrap \
  -H "Authorization: Bearer <access token>" \
  -H "Content-Type: application/json" \
  -d '{"key":"dev-bootstrap-key"}'
```

Remove `ADMIN_BOOTSTRAP_KEY` after creating the first admin.

## Tests

```bash
dotnet test tests/AF.Blood.UnitTests          # pure domain rules (78 tests)
dotnet test tests/AF.Blood.IntegrationTests   # real PostgreSQL/PostGIS via Testcontainers (51 tests)
cd web/af-foundation-site && npm run build && npm run check
cd mobile/af_blood_app && flutter analyze && flutter test
```

Integration tests start their own Postgres 17/PostGIS container — Docker must
be running.

## What is verified vs. what is not

Verified in this repository: backend build, 78 unit tests, 51 integration
tests against real PostgreSQL (identity, consent, matching distances,
concurrent acceptance, expiry, outbox retries/recovery, donation lifecycle,
deferral policy versioning, privacy, authorisation), Flyway migrations applied
to a clean database, website build + type-check, Flutter analyze + test.

**Not verified here** (needs credentials/devices): real OneSignal push
delivery, Android/iOS emulator or device runs, app-store builds. See
[known limitations](docs/known-limitations.md) before launch.

## Safety posture (read this first)

- The app coordinates **discovery only** — it never certifies medical
  eligibility, never operates a blood bank, and never replaces donation-centre
  screening.
- Donor location is generalised to a ~1 km grid **before storage**; precise
  coordinates are never persisted or exposed.
- A deferral countdown ending is **not** medical clearance; the UI says so
  explicitly.
- The deferral policy seeded for development is explicitly marked
  **NOT MEDICALLY APPROVED**. A qualified transfusion professional must
  approve the production policy before launch
  ([donation reporting & deferrals](docs/donation-reporting-and-deferrals.md)).
