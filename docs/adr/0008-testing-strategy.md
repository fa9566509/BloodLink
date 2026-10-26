# ADR-0008 — Testing strategy

Date: 2026-10-26 · Status: Accepted

## Decision

- **Unit tests** (xUnit) cover the pure domain: blood-group compatibility,
  request/donation state machines, deferral calculation, wave planning rules,
  coordinate generalization, backoff math.
- **Integration tests** run against **real PostgreSQL 17 + PostGIS** started
  by Testcontainers. Migrations are applied by executing the exact SQL files
  from `database/sql/` in version order (a SQL-runner in test code), and CI
 additionally runs real Flyway against a throwaway database. The API is
  exercised through `WebApplicationFactory<Program>` with its data source
  pointed at the test database.
- Isolation: one container per run; each test class creates a fresh database
  via `CREATE DATABASE … TEMPLATE migrated_template` (fast copy of the
  migrated schema), so tests cannot leak state into each other.
- Push delivery is tested against an in-memory `IPushDispatcher` fake — the
  real OneSignal is never called from tests.

## Not verified here

Real device push delivery, Android/iOS emulator UI flows, and app-store
builds require credentials/devices this environment does not have; they are
listed in docs/known-limitations.md rather than claimed as passing.
