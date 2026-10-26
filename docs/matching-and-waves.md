# Matching and notification waves

## Candidate selection

One parameterised PostGIS query (Dapper) selects candidates for a wave:

```sql
select dp.user_id, st_distance(dp.location, <request destination>) as distance_m
  from donor_profiles dp
 where dp.is_discoverable and dp.is_available and dp.location is not null
   and dp.blood_group = any(<compatible groups>)
   and st_dwithin(dp.location, <request destination>, <radius metres>)
   and dp.user_id <> @requesterId
   and not exists (active deferral for donor)
   and not exists (active account suspension)
   and not exists (membership in any run for this request)
   and not exists (response to this request)
 order by distance_m asc, dp.user_id asc   -- deterministic ordering
 limit @batchSize;
```

- **Compatibility** comes from `BloodCompatibility` (red-cell ABO/Rh), unit
  tested separately from the query and cross-checked against the
  `blood_group_compatibility` table by an integration test.
- **Determinism**: distance ascending, user id as stable tie-breaker. No
  machine-learning ranking.
- **Privacy**: the query returns user ids and distances only; coordinates
  never leave the database.

## Wave lifecycle

1. Request creation commits the request and wave-0 run (radius 25 km, due
   now) atomically.
2. The Worker claims due runs (`FOR UPDATE SKIP LOCKED`, 5-min lease), then
   **revalidates** the request: closed (`cancelled`/`expired`/`fulfilled`),
   `under_review`, or past `expires_at_utc` → wave stops.
3. Candidates are selected, membership rows inserted (idempotent), and one
   notification per donor is appended to history + outbox in the same
   transaction.
4. Wave rules (`WavePlanner`):
   - ≥ `MinCandidatesToStop` (10) newly-notified donors → stop.
   - otherwise, if a larger radius exists in the ladder (25 → 50 → 100) →
     schedule the next wave after `WaveDelayMinutes` (20).
   - top of the ladder → stop.
5. Lease recovery resets `processing` runs whose lease expired (worker crash).

## Staleness is expected and handled

Candidates are re-validated at **acceptance time** (consent, availability,
deferral, suspension) and before the donation workflow advances. A donor who
was notified yesterday may decline silently — that is fine; the request keeps
expanding its radius while open.

## Configuration

`Matching:RadiiKm` (default `25,50,100`), `WaveDelayMinutes` (20),
`BatchSizePerWave` (40), `MinCandidatesToStop` (10). These are operational
defaults, not medical or geographic rules.

## Donor-side browsing

`GET /requests/nearby` shows open requests within the donor's own radius
preference whose requested group can receive the donor's blood — ordered by
distance, carrying only the destination's generalized point and a whole-km
distance.
