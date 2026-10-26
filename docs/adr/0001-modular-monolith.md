# ADR-0001 — Modular monolith, single database, separate worker executable

Date: 2026-10-26 · Status: Accepted

## Decision

The platform is a **modular monolith**: one ASP.NET Core API executable, one
PostgreSQL database, and one .NET Worker Service executable that reuses the
same application and infrastructure assemblies. Feature boundaries are
enforced by folder/namespaces within the solution:

- `Contracts` — public DTOs and enums (no behaviour, no dependencies).
- `Application` — domain rules (compatibility matrix, state machines, deferral
  calculator, wave planner) and feature services that own SQL via Dapper.
- `Infrastructure` — adapters: ASP.NET Core Identity (EF Core), JWT tokens,
  OneSignal HTTP client, dev verification-code sender.
- `Api` / `Worker` — thin hosts that compose the above.

## Rationale

Target scale (10k users in a year, Pakistan-wide) is far below the threshold
where microservices pay for themselves. A modular monolith keeps transaction
boundaries real (one database), deploys as three containers (api, worker,
flyway job), and leaves room to extract modules later if evidence demands it.

Redis, Kafka, Kubernetes, and external message brokers are deliberately
excluded for v1; the PostgreSQL-backed transactional outbox plus
`FOR UPDATE SKIP LOCKED` leasing provides durable background work without a
broker (ADR-0006).
