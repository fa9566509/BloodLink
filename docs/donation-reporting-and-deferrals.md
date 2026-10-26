# Donation reporting, verification and deferral policy

## Statuses and provenance

A donation record always carries *who reported it* and its verification
state. The states (and legal transitions) live in `DonationStateMachine`:

```
reported ── requester ack ──► completed_unverified ── verifier ──► verified
   │                              │
   ├── requester dispute ──► disputed ── moderation ──► completed_unverified
   │                              │                    ├── verified
   ├── verifier (direct) ──► verified                  └── rejected
   ├── moderator ──► rejected
   └── administrator correction ──► corrected  (from any counted state)

awaiting_confirmation — reserved for v2 acknowledgement escalation; not
produced by v1 logic (documented, tested as not-allowed in transitions).
```

Provenance rule: **a self-reported donation is never presented as verified.**
Verification is an explicit act by an administrator/donation_verifier.

## Deferral policies are data, not code

- `deferral_policy_versions` stores approved versions: `deferral_days`,
  `provisional_days`, `effective_from`, `approved_by`, `approval_reference`.
- One active version per component (`whole_blood`). Administrators create and
  activate versions via `/api/v1/admin/policies`; the approval reference is
  mandatory.
- Every donation record stores **the policy version that was applied to it**;
  activating a new policy never rewrites existing records.
- Intervals are never hard-coded. The seeded row (`DEV-SEED-0.1-whole-blood`)
  is a clearly-marked development placeholder — **not** an approved medical
  policy.

## Provisional vs verified restriction

| Event | Restriction applied |
|-------|--------------------|
| Donor reports a donation | `provisional` deferral (policy.provisional_days; 0 = none — an explicit policy decision) |
| Requester acknowledges | unchanged (still provisional) |
| Verifier confirms | provisional revoked; `verified` deferral with full `deferral_days` from end of donation date |
| Moderator rejects | provisional revoked (donor freed) |
| Admin corrects the donation date | deferrals recomputed from the same policy; `corrected` state treated conservatively as a full donation |

## Hard safety rules (enforced + tested)

1. **Acceptance never starts a deferral.** Only a donation report can.
2. The donation record is independent from the request's operational state
   (`fulfilled` is bookkeeping; eligibility derives from `donor_deferrals`).
3. **Countdown expiry ≠ medical clearance.** Every eligibility surface ends
   with: *"Your recorded deferral period has ended. Final eligibility is
   determined by the donation centre."*
4. Eligibility is always derived from persisted state — no scheduled job is
   trusted to "unlock" a donor on time.
5. Corrections append events; records are never silently overwritten.
   `donation_record_events` and `audit_log` are append-only at the database.

## Who can do what

| Action | Actor | Endpoint |
|--------|-------|----------|
| Report outcome | the accepting donor | `POST /requests/{id}/donation-report` |
| Acknowledge / dispute | request owner | `POST /requests/{id}/donation-acknowledgement` |
| Verify / reject | moderator, verifier, admin | `POST /admin/donations/{id}/verify|reject` |
| Correct | administrator | `POST /admin/donations/{id}/correct` |
| Manage policies | administrator | `POST /admin/policies`, `POST /admin/policies/{id}/activate` |

## Before production

A qualified transfusion professional (WHO donor-selection guidance + local
Pakistan/Punjab requirements) must approve the production policy values. Then:

1. Create the policy version with real values and a real approval reference.
2. Activate it (the dev seed is deactivated automatically).
3. Remove or deactivate the dev seed version for cleanliness.
