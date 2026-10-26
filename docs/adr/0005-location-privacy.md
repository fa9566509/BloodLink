# ADR-0005 — Location privacy: generalized points only

Date: 2026-10-26 · Status: Accepted

## Decision

- Donor location is **generalized at write time**: the server truncates
  coordinates to 2 decimal places (~1.1 km grid) and stores only the
  generalized point. Precise coordinates are never persisted.
- No API returns a donor's coordinates to anyone other than the donor
  themselves (owner reads see the same generalized point that was stored).
- Request destinations are stored at the same 2-dp precision. Donors browsing
  requests see the destination label, an approximate point, and the distance
  rounded to whole kilometres — never the donor's own location and never
  precise requester coordinates.
- Location permission is requested by the mobile app only when the user opts
  into discovery, with a manual map-point/pin fallback.
- Consent (`consent_version`, `consented_at_utc`) is recorded with the
  profile; opting out immediately removes the donor from new matching runs
  (running waves revalidate consent before notifying).

## Rationale

Matching at ~1 km granularity is more than sufficient for 25–100 km radii,
and storage-side generalization means a database leak cannot expose home
addresses. Raw GPS accuracy would buy nothing for the matching algorithm.
