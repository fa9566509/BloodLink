# ADR-0006 — Transactional outbox + SKIP LOCKED leasing for background work

Date: 2026-10-26 · Status: Accepted

## Decision

All side effects that leave the API process (push notifications) are written
as rows into `notification_outbox` **inside the same transaction** as the
state change that caused them. The worker delivers them later:

- Claiming: `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED …)` with a
  `leased_until` deadline.
- Delivery: OneSignal REST API (via `IPushDispatcher` port; no-op logger when
  unconfigured).
- Retry: bounded exponential backoff with jitter (`5s * 2^attempts`, capped),
  max 8 attempts → `failed`.
- Recovery: a reaper resets `processing` rows whose lease expired (worker
  crash mid-send).
- Delivery is at-least-once; OneSignal dedupes repeated notifications to the
  same subscription. Never assume exactly-once.

The same leasing pattern drives `matching_runs` (wave scheduling), and
request expiry is a periodic `UPDATE … WHERE expires_at_utc <= now()` scan.
No external scheduler, no broker.

## Why not Redis/Kafka

At v1 scale (10k users), PostgreSQL provides durability, visibility
(`SELECT`), and atomic claiming. A broker would add an operational failure
domain without removing the need for the outbox (delivery to OneSignal would
still be retryable).
