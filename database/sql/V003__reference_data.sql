-- =============================================================================
-- V003 — Reference data: roles, blood-group compatibility matrix, dev policy
-- =============================================================================
-- Seed rule: only NON-SENSITIVE reference data is seeded here. No users are
-- seeded. The deferral policy below is a clearly-marked DEVELOPMENT seed so the
-- application can be exercised end to end; it is NOT an approved medical
-- policy and must be replaced before production use (see
-- docs/donation-reporting-and-deferrals.md).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Roles. Every authenticated user is implicitly a "member" (no role row needed).
-- -----------------------------------------------------------------------------
insert into "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp") values
    (gen_random_uuid(), 'moderator',         'MODERATOR',         gen_random_uuid()::text),
    (gen_random_uuid(), 'donation_verifier', 'DONATION_VERIFIER', gen_random_uuid()::text),
    (gen_random_uuid(), 'administrator',     'ADMINISTRATOR',     gen_random_uuid()::text);

-- -----------------------------------------------------------------------------
-- Blood-group compatibility (red-cell). recipient_group may receive
-- donor_group blood when is_compatible = true.
-- Matrix: O- gives to everyone; AB+ receives from everyone; Rh+ recipients may
-- receive Rh- blood; Rh- recipients may not receive Rh+ blood.
-- The application ALSO carries this matrix in code
-- (src/AF.Blood.Application/Domain/BloodCompatibility.cs); an integration test
-- asserts the two representations agree. Do not change one without the other.
-- -----------------------------------------------------------------------------
insert into blood_group_compatibility (recipient_group, donor_group, is_compatible) values
    -- O- recipient
    ('O-','O-',true),  ('O-','O+',false), ('O-','A-',false), ('O-','A+',false),
    ('O-','B-',false), ('O-','B+',false), ('O-','AB-',false),('O-','AB+',false),
    -- O+ recipient
    ('O+','O-',true),  ('O+','O+',true),  ('O+','A-',false), ('O+','A+',false),
    ('O+','B-',false), ('O+','B+',false), ('O+','AB-',false),('O+','AB+',false),
    -- A- recipient
    ('A-','O-',true),  ('A-','O+',false), ('A-','A-',true),  ('A-','A+',false),
    ('A-','B-',false), ('A-','B+',false), ('A-','AB-',false),('A-','AB+',false),
    -- A+ recipient
    ('A+','O-',true),  ('A+','O+',true),  ('A+','A-',true),  ('A+','A+',true),
    ('A+','B-',false), ('A+','B+',false), ('A+','AB-',false),('A+','AB+',false),
    -- B- recipient
    ('B-','O-',true),  ('B-','O+',false), ('B-','A-',false), ('B-','A+',false),
    ('B-','B-',true),  ('B-','B+',false), ('B-','AB-',false),('B-','AB+',false),
    -- B+ recipient
    ('B+','O-',true),  ('B+','O+',true),  ('B+','A-',false), ('B+','A+',false),
    ('B+','B-',true),  ('B+','B+',true),  ('B+','AB-',false),('B+','AB+',false),
    -- AB- recipient
    ('AB-','O-',true), ('AB-','O+',false),('AB-','A-',true), ('AB-','A+',false),
    ('AB-','B-',true), ('AB-','B+',false),('AB-','AB-',true),('AB-','AB+',false),
    -- AB+ recipient (universal recipient)
    ('AB+','O-',true), ('AB+','O+',true), ('AB+','A-',true), ('AB+','A+',true),
    ('AB+','B-',true), ('AB+','B+',true), ('AB+','AB-',true),('AB+','AB+',true);

-- -----------------------------------------------------------------------------
-- DEVELOPMENT-ONLY deferral policy seed.
--   ⚠ NOT MEDICALLY APPROVED. NOT FOR PRODUCTION. ⚠
-- Values are placeholders so the deferral engine can be tested. Before
-- production launch, an administrator must create and activate a policy
-- version carrying a real approval reference from a qualified transfusion
-- professional (POST /api/v1/admin/policies).
-- -----------------------------------------------------------------------------
insert into deferral_policy_versions
    (version, component_type, deferral_days, provisional_days, effective_from,
     is_active, approved_by, approval_reference, notes)
values
    ('DEV-SEED-0.1-whole-blood', 'whole_blood', 90, 7, date '2026-01-01',
     true,
     'LOCAL DEVELOPMENT SEED — NOT MEDICALLY APPROVED',
     'DEV-SEED-NOT-FOR-PRODUCTION',
     'Placeholder intervals for local development only: 90-day whole-blood ' ||
     'deferral after a VERIFIED donation, 7-day provisional restriction after ' ||
     'a self-reported (unverified) donation. Must be replaced by a policy ' ||
     'version approved by a qualified transfusion professional before ' ||
     'production use.');
