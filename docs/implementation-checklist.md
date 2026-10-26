# Implementation checklist — final status

Status: `[x]` done and verified · `[~]` implemented, verification limited
(nothing is marked done unless it was actually observed working).

## Phase 1 — Inspect and establish

- [x] Repository confirmed empty (greenfield); git initialized (branch `main`).
- [x] Toolchain: .NET SDK 10.0.403, Node 24.20, Flutter 3.44.8/Dart 3.12.2, Docker 29.5.3.
- [x] Solution `AF.Blood.slnx` (Api / Application / Contracts / Infrastructure / Worker
      + UnitTests / IntegrationTests); references and packages resolved.
- [x] ADRs 0001–0008 (docs/adr/).
- [x] Docker Compose (postgres+postgis, flyway job, api, worker, web) + .env.example.

## Phase 2 — Database and Identity

- [x] V001 Identity schema (hand-written EF-compatible SQL, ADR-0003).
- [x] V002 application schema (all tables per spec §12, CHECK constraints,
      partial unique indexes, append-only triggers on audit/donation history).
- [x] V003 reference data (roles, 64-row compatibility matrix, DEV-SEED policy
      clearly marked not-medically-approved).
- [x] Flyway applied 3 migrations to a clean PostgreSQL 17.5/PostGIS database
      via `docker compose run --rm flyway migrate` (observed).
- [x] Integration test proves UserManager works against the Flyway schema and
      seeded roles resolve.

## Phase 3 — Core application workflows

- [x] Contracts: DTOs/enums for all modules.
- [x] Domain: BloodCompatibility, RequestStateMachine, DonationStateMachine,
      DeferralCalculator, WavePlanner, GeoGeneralizer, Backoff (unit-tested).
- [x] Auth: register/login/refresh-rotation-with-reuse-detection/logout/
      verification codes/password recovery/change, lockout, bootstrap-admin
      (integration-tested end to end).
- [x] Donor profiles: upsert, consent grant/withdrawal (location generalized
      to ~1 km and deleted on withdrawal), availability, eligibility read with
      mandated safety wording.
- [x] Requests: create (validations, per-user open cap), nearby browse
      (compatibility + radius + distance ordering), detail, cancel (owner only),
      concurrency-safe accept (row lock; exactly-one-winner verified), decline,
      duplicate-response protection, server-side expiry.
- [x] Matching: PostGIS candidate query (ST_DWithin, compatibility, deferral,
      suspension, dedup), wave scheduling (radius ladder 25→50→100, stop rules),
      lease recovery, cancelled-request stop.
- [x] Donation reporting: report → provisional restriction → acknowledge →
      verify/reject/correct; append-only events; policy versioning (records keep
      the policy that applied to them); deferral exclusion from matching.
- [x] Notifications: history, unread counts, mark-read (ownership enforced),
      devices (subscription ownership transfer, disable).
- [x] Admin: abuse reports, suspend/restore (revokes sessions; admins protected),
      policy management, donation review.

## Phase 4 — Notifications and worker

- [x] Transactional outbox (enqueue within business transaction).
- [x] Worker jobs: matching-run processor, outbox delivery (claim → send →
      retry with jittered backoff → recover abandoned leases → skip without
      devices), request expiry, housekeeping (stale codes/tokens).
- [x] OneSignal HTTP adapter + no-op dev dispatcher; deep links; payloads carry
      no sensitive data; delivered_at recorded only on real provider acceptance.

## Phase 5 — Clients

- [x] Flutter app: welcome/login/register/recovery/verification, donor profile +
      consent (GPS with manual fallback, permission only when needed),
      availability toggle, eligibility/deferral countdown, nearby requests,
      request create/detail/accept/decline/cancel, my requests/responses,
      donation reporting, donation history, notification inbox, push devices,
      account/settings/sign-out, consistent loading/error/empty/offline states.
      `flutter analyze`: no issues. `flutter test`: passing.
      (Native builds and on-device runs NOT executed — no Android SDK/device.)
- [x] TanStack landing site: mission, how-it-works, download, FAQs, safety,
      privacy, terms, contact; placeholders for org-specific facts.
      `npm run build` + `npm run check`: clean; static serving smoke-tested.

## Phase 6 — Hardening and delivery

- [x] Release build of the full solution.
- [x] 78 unit tests + 51 integration tests green (real PostgreSQL via Testcontainers).
- [x] API Docker image builds (multi-stage, non-root).
- [x] CI pipeline (backend build+tests, Flyway migrate+validate, website,
      flutter analyze/test).
- [x] Documentation: README, architecture, local dev, env reference, database +
      Flyway, API, auth design, matching/waves, donation/deferrals, location
      privacy, OneSignal, Flutter build, app-store checklist, Coolify deploy,
      backup/restore, security/privacy checklist, launch configuration,
      known limitations.

## Remaining before public launch (external dependencies)

- Configure OneSignal credentials and verify real device delivery.
- Android SDK/Xcode builds + on-device journey runs.
- Real email/SMS provider for verification codes.
- Medically approved deferral policy version (replace DEV-SEED).
- Legal review of privacy/terms placeholders; supply organisation facts
  (docs/launch-configuration.md).
- Production secrets + first admin bootstrap, then remove the bootstrap key.
