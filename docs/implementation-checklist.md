# Implementation checklist — live progress

Status marker legend: `[x]` done and verified · `[~]` implemented, verification pending ·
`[ ]` not started. This file is the source of truth for build progress and is updated
as work proceeds. Nothing here is marked done unless it was actually observed working.

## Phase 1 — Inspect and establish

- [x] Repository confirmed empty (greenfield); git initialized (branch `main`).
- [x] Toolchain: .NET SDK 10.0.403, Node 24.20, Flutter 3.44.8/Dart 3.12.2, Docker 29.5.3.
- [x] Solution `AF.Blood.sln` with Api / Application / Contracts / Infrastructure / Worker
      + UnitTests / IntegrationTests; references and packages resolved.
- [x] ADRs 0001–0008 written (docs/adr/).
- [x] Docker Compose (postgres+postgis, flyway job, api, worker, web) and .env.example.

## Phase 2 — Database and Identity

- [x] V001 Identity schema (hand-written EF-compatible SQL, ADR-0003).
- [x] V002 application schema (all tables per spec §12, CHECK constraints,
      partial unique indexes, append-only triggers on audit/donation history).
- [x] V003 reference data (roles, 64-row compatibility matrix, DEV-SEED policy
      clearly marked not-medically-approved).
- [~] Flyway migration run against a real PostgreSQL 17/PostGIS database (compose up).
- [~] Integration test: empty-database migration + Identity UserManager works.

## Phase 3 — Core application workflows

- [~] Contracts: DTOs/enums for all modules.
- [~] Domain: BloodCompatibility, RequestStateMachine, DonationStateMachine,
      DeferralCalculator, WavePlanner, GeoGeneralizer (+ unit tests).
- [~] Auth: register/login/refresh/logout/verify/password-recovery, roles,
      lockout, JWT bearer, bootstrap-admin.
- [~] Donor profiles: upsert, consent, availability, eligibility read.
- [~] Requests: create/browse/detail/cancel + concurrency-safe accept/decline.
- [~] Matching: candidate query (PostGIS, ST_DWithin), wave scheduling rules.
- [~] Donation reporting: report/acknowledge/dispute/admin verify-reject-correct,
      policy-backed deferrals, append-only history, audit.
- [~] Notifications: history, devices, outbox enqueue.
- [~] Admin: abuse reports, suspensions, policies.

## Phase 4 — Notifications and worker

- [~] Transactional outbox (enqueue within business transaction).
- [~] Worker: matching-run processor, outbox delivery, request expiry, lease recovery,
      refresh-token housekeeping; graceful shutdown.
- [~] OneSignal adapter (HTTP) + no-op dev dispatcher; deep links; no sensitive payloads.

## Phase 5 — Clients

- [~] Flutter app (principal screens per spec §15; analyze/test when possible).
- [~] TanStack landing website (build verified).

## Phase 6 — Hardening and delivery

- [ ] Full backend build + unit tests green.
- [ ] Full integration suite green against real PostgreSQL.
- [ ] Website production build green.
- [ ] Flutter analyze/test green.
- [ ] Docs complete (README, local dev, env reference, deploy, backup, security checklist).
- [ ] Final summary of actual verified state.

## Known environment constraints

- No Android SDK/iOS toolchain on this machine: Flutter app is verified with
  `flutter analyze` + `flutter test`; native builds and device flows are NOT claimed.
- OneSignal credentials not available: adapter implemented against documented REST API,
  covered by a fake in tests; real delivery must be configured before launch.
