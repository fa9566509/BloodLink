# Coolify deployment

## Overview

`deploy/coolify/docker-compose.coolify.yml` defines four services:
`postgres` (persistent volume), `flyway` (one-shot migration on every
deploy), `api`, `worker`, and the static `web` site. It mirrors the dev
compose but expects real secrets from Coolify's environment UI.

## Steps

1. **Create a project + resource** of type *Docker Compose* in Coolify,
   pointing at the repository (or paste the compose file).
2. **Environment variables** — set the required ones
   ([full reference](environment-variables.md)):
   - `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
   - `JWT_KEY` (`openssl rand -hex 32`)
   - `JWT_ISSUER`, `JWT_AUDIENCE`
   - `CORS_ALLOWED_ORIGINS` (public site origin)
   - `ADMIN_BOOTSTRAP_KEY` (only until the first admin exists)
   - `ONESIGNAL_APP_ID`, `ONESIGNAL_API_KEY` (or leave empty to start without push)
   - `MATCHING_RADII_KM` (optional)
   - `VITE_API_BASE_URL` (public API URL for the site build)
3. **Volumes** — Coolify maps the compose volume `postgres_data` to a
   persistent disk. Verify it before first deploy; **off-host backups are
   still mandatory** ([backup guide](backup-and-restore.md)).
4. **Domains / TLS** — attach your domain to the `api` service (port 8080)
   and the `web` service (port 80); Coolify provisions HTTPS via Traefik.
   Set `Cors__AllowedOrigins` to the site's https origin.
5. **Deploy.** The `flyway` job runs before api/worker start (they
   `depends_on: service_completed_successfully`), so the schema is always
   current when the binaries boot. The API fails fast on a missing/weak
   `Jwt__Key` rather than degrading mid-request.
6. **Health** — `GET /health/live` (process) and `GET /health/ready`
   (database + PostGIS). Configure Coolify health checks on the api service.
7. **First admin** — register through the app, then call `/api/v1/admin/bootstrap`
   with the key; afterwards **remove `ADMIN_BOOTSTRAP_KEY`** and redeploy.

## Multiple workers?

Run **one** worker container. Leases make accidental double-processing safe,
but a single worker keeps wave pacing deterministic. Scaling the API is fine
(stateless).

## Database user

For production, prefer a dedicated non-superuser application account with
DML on the schema (see [database docs](database-schema.md)). The PostGIS
extension must exist before V002 runs; if the Coolify-provisioned database
user is not a superuser, create the extension once via a superuser session.

## Migrations on deploy

Flyway runs as a separate one-shot job — it is safe to redeploy repeatedly:
applied migrations are skipped, and `validate` semantics catch drift. Never
edit an applied migration file; add a new one instead.
