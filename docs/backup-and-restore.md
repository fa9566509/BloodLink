# Backup and restore

## What matters

The PostgreSQL data volume (`postgres_data`) is the only stateful component.
Everything else (api, worker, web) is stateless and rebuildable from the
repository; migration state lives inside the database itself.

## Backup strategy (off-host, mandatory)

On-host volume snapshots alone do not survive host loss. Schedule **daily
logical backups copied off the host**:

```bash
# On the host running the postgres container (cron/systemd timer):
docker exec aff-postgres pg_dump -U aff -d aff_blood -Fc \
  -f /tmp/aff_blood_$(date +%F_%H%M).dump
docker cp aff-postgres:/tmp/aff_blood_$(date +%F_%H%M).dump \
  /backups/   # then sync /backups to object storage or another machine

# Retention: e.g. 14 daily + 8 weekly. Verify restores quarterly (below).
```

Alternatives: Coolify's built-in Postgres backup feature, `wal-g`/`pgBackRest`
for point-in-time recovery (recommended once traffic grows), or managed
PostgreSQL.

## Restore procedure

```bash
# 1. Stop the writers (api + worker) to avoid divergent state.
docker compose stop api worker

# 2. Recreate a clean database (PostGIS extension needed before V002-era data).
docker compose up -d postgres
docker compose run --rm flyway migrate     # brings schema to latest

# 3. Restore the dump over it.
docker exec -i aff-postgres pg_restore -U aff -d aff_blood --clean --if-exists \
  < /backups/aff_blood_YYYY-MM-DD_HHMM.dump

# 4. Validate, then start the services.
docker compose start api worker
curl -f http://127.0.0.1:8080/health/ready
```

Notes:
- `pg_restore --clean --if-exists` replaces objects present in the dump.
  If the dump predates a newer migration, **restore first, then migrate**
  (swap steps 2–3) — decide based on which is newer.
- Record the RPO you actually achieve (backup interval) and the RTO
  (restore drill time). For a blood-coordination service, a realistic target
  is RPO ≤ 24 h, RTO ≤ 1 h.

## Verification

A backup that has never been restored is a hope, not a backup. Quarterly:
restore the latest dump into a scratch container, run the API against it,
log in as a test account, and confirm `/health/ready` plus a spot-check
query (e.g. `select count(*) from donation_requests`).

## What is not in the database

- Push subscriptions live partly in OneSignal's cloud; after a full restore,
  devices re-register on next app login, or re-register by re-sending the
  subscription from the app.
- Static site + API images are rebuilt from the repository.
