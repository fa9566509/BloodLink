# ADR-0007 — Website: TanStack Router + Vite + React (static)

Date: 2026-10-26 · Status: Accepted

## Decision

The public website uses **TanStack Router** (file-based routes via the Vite
plugin) + **React 18 + TypeScript**, built with Vite to fully static assets
served by nginx in Docker.

## Alternatives considered

- **TanStack Start** — full SSR framework; more capability than a landing
  site needs and complicates Coolify static hosting.
- Plain Vite + no router — loses typed routing and clean URL sections
  (`/download`, `/faq`, `/privacy`).

TanStack Router gives typed navigation, preloading, and a documented,
modern TanStack-based stack while remaining deployable as static assets.

Organisation-specific facts (addresses, phone numbers, store links) come from
`web/af-foundation-site/src/config/site.ts` placeholders; the launch
checklist lists every value AF Foundation must supply. Nothing official is
invented.
