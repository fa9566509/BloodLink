# ADR-0004 — Application services own SQL via Dapper; no generic repositories

Date: 2026-10-26 · Status: Accepted

## Decision

Feature services in `AF.Blood.Application` take an `NpgsqlDataSource` and use
**Dapper with parameterised SQL** directly. There is no repository layer and
no store interface ceremony: each feature service is the explicit
transaction boundary for its workflow (`BEGIN` → lock/read → rules → write →
outbox → `COMMIT`).

`AF.Blood.Infrastructure` is reserved for adapters that genuinely replaceable
behaviour: Identity (EF Core), JWT issuance, OneSignal HTTP, dev code sender.

## Rationale

Repository abstractions wrapping Dapper add a second language per feature
without adding testability (integration tests use real PostgreSQL anyway).
The tradeoff — `Npgsql`/`Dapper` referenced by Application — is accepted and
documented; the domain rules that matter (compatibility, state machines,
deferral math, wave planning) stay in pure, unit-tested classes under
`Application/Domain/` with zero I/O.
