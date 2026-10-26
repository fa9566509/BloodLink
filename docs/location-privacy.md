# Location privacy (ADR-0005)

## What is stored

- Donor location: **generalized at write time** to 2 decimal places
  (~1.1 km grid). `GeoGeneralizer.Generalize` is applied in the consent
  endpoint before any SQL runs; raw coordinates are never persisted.
- Request destination: same 2 dp generalization.

## What is exposed

| Consumer | Sees |
|----------|------|
| Donor (own profile) | The same generalized point that was stored |
| Other users | **Never** any donor coordinates |
| Donors browsing requests | Destination label + generalized destination + whole-km distance |
| Matching output | User ids + distances inside the service; notifications carry no coordinates |
| Push payloads | No coordinates, no medical details, no phone numbers, no tokens |

## Consent lifecycle

- Enabling discovery requires an explicit `consent_version` +
  timestamp + an approximate location (GPS or manual fallback in the app).
- Location permission is requested by the app **only** at that moment, with a
  manual lat/lon fallback.
- Withdrawing consent: `is_discoverable=false`, availability reset, consent
  fields cleared, **stored location deleted**. Donor disappears from all new
  matching immediately; running waves revalidate consent before notifying.

## Verification

Integration tests assert:
- only the rounded point exists in the database after a precise registration
  (`Matching_output_never_contains_raw_candidate_locations`);
- nearby browsing never returns donor coordinates;
- withdrawn donors are excluded from candidate selection.
