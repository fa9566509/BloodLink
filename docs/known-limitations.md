# Known limitations and v2 backlog

## Not verified in this repository (be honest about these)

1. **Real push delivery** — OneSignal adapter implemented against the
   documented REST API and covered by a test fake; no real OneSignal account
   was available, so device delivery is unverified.
2. **Mobile device/emulator runs** — Flutter analyze + widget tests pass; no
   Android/iOS build or on-device journey was executed here (no Android
   SDK/device in the environment). Native `flutter build` and store
   submissions are untested.
3. **Production secret hygiene** — dev configuration intentionally contains
   well-known insecure values; the production posture depends on executing
   [security checklist](security-privacy-checklist.md).
4. **Load/performance testing** — schema and queries are indexed for the
   stated scale, but no benchmark was run.

## Accepted v1 limitations (by design)

- One donor slot per request; requesters needing more donors create separate
  requests.
- No multi-unit fulfilment, hospital integrations, or blood inventory.
- Moderation is basic: abuse reports + suspension. Dispute resolution is
  manual via the admin endpoints.
- `awaiting_confirmation` donation state is reserved (documented) but unused
  by v1 logic.
- Email/phone verification uses pluggable code delivery — dev logs codes.
- Worker assumes a single instance per deployment (leases make mistakes safe
  but are not high-throughput coordination).
- Refresh-token revocation leaves access tokens valid up to 15 minutes.

## v2 backlog (from the founder's deferrals)

- [ ] Mandatory hospital/blood-centre verification for requesters
- [ ] Independent verification of donations before reports are trusted
      (partnership with centres; verification evidence upload)
- [ ] Advanced fraud scoring (velocity checks, device fingerprints)
- [ ] Richer moderation workflows (queues, appeals, audit UI)
- [ ] Automated hospital/blood-bank integrations
- [ ] Blood inventory management
- [ ] Multi-unit fulfilment and multi-donor request capacity
- [ ] Analytics and recommendation models (e.g. donor re-engagement)
- [ ] Localisation framework (Urdu UI strings; architecture already keeps
      user-facing copy in one place per screen)
- [ ] Acknowledgement escalation flow using the reserved
      `awaiting_confirmation` state
- [ ] Configurable retention/deletion procedures for audit + donation data
- [ ] Account deletion self-service

## Technical debt worth noting

- `web/af-foundation-site` has no automated UI tests (static content; build +
  type-check only).
- The landing site links are placeholders until the store URLs exist.
- Notification emails/SMS are not sent — push + in-app inbox only.
- `MatchingService` failure handling retries a failed run indefinitely
  (re-claimed each cycle after lease recovery) — a poison-run counter exists
  but is advisory; consider a bounded retry column in v2.
