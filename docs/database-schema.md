# Database schema and Flyway

## Principles

- **Flyway is the only schema mechanism** (ADR-0003). EF Core migrations do
  not exist; the API and Worker never modify the schema.
- Versioned SQL lives in `database/sql/V00N__name.sql`. Migrations are plain
  SQL and must run as a single batch per file.
- Application tables use lowercase `snake_case`; ASP.NET Core Identity tables
  keep EF's quoted PascalCase names (`"AspNetUsers"` etc.) to match EF Core's
  Npgsql conventions exactly (verified by the `Identity_works_against_the_flyway_created_schema`
  integration test).
- UUID primary keys (`gen_random_uuid()`), UTC `timestamptz` columns,
  CHECK constraints for enum-like values, partial indexes for hot queries.

## Migration files

| Version | Content |
|---------|---------|
| `V001__identity_schema.sql` | ASP.NET Core Identity tables (Users, Roles, UserRoles, Claims, Logins, Tokens) + `DisplayName`, `CreatedAtUtc`, `UpdatedAtUtc` |
| `V002__app_schema.sql` | donor_profiles, deferral_policy_versions, donation_requests, request_responses, blood_group_compatibility, donation_records (+ append-only events), donor_deferrals, matching_runs (+ membership), notification_history, notification_outbox, user_devices, abuse_reports, account_suspensions, auth_refresh_tokens, verification_codes, audit_log (append-only trigger) |
| `V003__reference_data.sql` | Roles (moderator, donation_verifier, administrator), the 64-row red-cell compatibility matrix, and the **DEV-SEED** deferral policy (explicitly not medically approved) |

## Running migrations

Local/dev (Docker Compose):

```bash
docker compose run --rm flyway migrate
```

Standalone Flyway CLI:

```bash
flyway -url=jdbc:postgresql://localhost:5433/aff_blood \
       -user=aff -password=... \
       -locations=filesystem:database/sql migrate
```

CI runs `migrate` followed by `validate` against a clean PostGIS container
(`.github/workflows/ci.yml`), and the integration test suite applies the same
SQL through Npgsql to a template database.

## Rollback / recovery philosophy

Flyway community edition has no automatic down-migrations, and irreversible
migrations must not pretend otherwise. The recovery strategy:

1. **Additive changes** (new table/column/index) — safe; the previous
   application version ignores them. Roll forward, do not roll back.
2. **Destructive changes** (drop/alter column semantics) — require a planned
   migration window: restore from backup if the change must be undone
   ([backup guide](backup-and-restore.md)).
3. **Bad migration already applied** — create a NEW versioned migration that
   repairs the schema. Never edit an applied migration file (checksum
   validation will fail, by design).
4. **Flyway history corruption** — `flyway validate` fails loudly; repair
   with `flyway repair` only after understanding the drift.

## Grants / least privilege (production)

Run the application with a dedicated non-superuser account that owns
`public` (or has `CREATE` on it) plus DML rights on application tables.
`CREATE EXTENSION postgis` needs superuser — execute it once at database
creation (the Docker image's `POSTGRES_USER` does this for its init database;
for a custom database run it manually before V002).

```sql
-- example: dedicated app role
create role aff_app login password '...';
grant connect on database aff_blood to aff_app;
\c aff_blood
grant usage, create on schema public to aff_app;   -- schema owner, or grant per-table DML
-- append-only protection is enforced by triggers, not grants, so it holds
-- regardless of role.
```

## Schema highlights

- `donor_profiles.location` — `geography(Point,4326)`, generalized to 2 dp at
  write time; GiST index partially on discoverable+available donors.
- `donation_requests.destination` — same type; partial GiST on `status='open'`
  for nearby browsing; `expires_at_utc` partial index for the expiry job.
- `request_responses` — `UNIQUE (request_id, donor_user_id)` makes duplicate
  responses impossible at the storage layer.
- `donation_records` — partial unique index: one non-rejected record per
  request (v1 single donor slot).
- `donor_deferrals` — active deferrals are rows with `revoked_at_utc IS NULL`;
  matching and acceptance check `eligible_again_at > now()`.
- `notification_outbox` / `matching_runs` — lease columns (`leased_by`,
  `leased_until`) + partial indexes power `FOR UPDATE SKIP LOCKED` claiming.
- `audit_log`, `donation_record_events` — append-only enforced by
  `aff_prevent_mutation()` triggers that reject UPDATE/DELETE at the DB level.
