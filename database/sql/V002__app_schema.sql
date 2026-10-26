-- =============================================================================
-- V002 — Application schema (donor profiles, requests, responses, donations,
--        deferrals, matching, notifications, devices, moderation, audit)
-- =============================================================================
-- Conventions:
--   * Unquoted lowercase snake_case identifiers.
--   * UUID primary keys via gen_random_uuid() (pgcrypto built into PG13+).
--   * All timestamps are UTC (`timestamp with time zone`), named *_utc.
--   * PostGIS geography(Point,4326) for coordinates, GiST indexes for proximity.
--   * Enum-like values enforced with CHECK constraints.
--   * Append-only tables (audit_log, donation_record_events) get triggers that
--     reject UPDATE/DELETE at the database level.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Donor profiles (privacy: `location` is an approximate, generalized point;
-- raw precise coordinates are never stored, see docs/location-privacy.md)
-- -----------------------------------------------------------------------------
create table donor_profiles (
    id                 uuid primary key default gen_random_uuid(),
    user_id            uuid not null references "AspNetUsers" ("Id") on delete cascade,
    blood_group        text not null check (blood_group in ('A+','A-','B+','B-','AB+','AB-','O+','O-')),
    is_discoverable    boolean not null default false,
    is_available       boolean not null default false,
    location           geography(point, 4326),
    search_radius_km   integer not null default 25 check (search_radius_km between 1 and 100),
    consent_version    text,
    consented_at_utc   timestamptz,
    created_at_utc     timestamptz not null default now(),
    updated_at_utc     timestamptz not null default now(),
    -- A discoverable donor must have recorded consent and a location.
    constraint donor_profiles_discoverable_check check (
        is_discoverable = false
        or (consent_version is not null and consented_at_utc is not null and location is not null)
    )
);

create unique index donor_profiles_user_id_uq on donor_profiles (user_id);
create index donor_profiles_discoverable_gix on donor_profiles using gist (location)
    where is_discoverable and is_available;

-- -----------------------------------------------------------------------------
-- Deferral policy versions (configurable; values are supplied by an approved
-- policy owner — the dev seed is explicitly NOT a medical policy)
-- -----------------------------------------------------------------------------
create table deferral_policy_versions (
    id                uuid primary key default gen_random_uuid(),
    version           text not null unique,
    component_type    text not null default 'whole_blood' check (component_type = 'whole_blood'),
    deferral_days     integer not null check (deferral_days between 0 and 365),
    provisional_days  integer not null default 0 check (provisional_days between 0 and 365),
    effective_from    date not null,
    is_active         boolean not null default false,
    approved_by       text not null,
    approval_reference text not null,
    notes             text,
    created_at_utc    timestamptz not null default now()
);

-- One active policy per component type.
create unique index deferral_policy_versions_active_uq on deferral_policy_versions (component_type)
    where is_active;

-- -----------------------------------------------------------------------------
-- Donation requests (whole blood only, one donor slot in v1)
-- -----------------------------------------------------------------------------
create table donation_requests (
    id                    uuid primary key default gen_random_uuid(),
    requester_id          uuid not null references "AspNetUsers" ("Id") on delete cascade,
    blood_group           text not null check (blood_group in ('A+','A-','B+','B-','AB+','AB-','O+','O-')),
    component_type        text not null default 'whole_blood' check (component_type = 'whole_blood'),
    destination           geography(point, 4326) not null,
    destination_label     text not null check (char_length(destination_label) between 1 and 200),
    facility_name         text check (facility_name is null or char_length(facility_name) <= 200),
    reference_note        text check (reference_note is null or char_length(reference_note) <= 500),
    urgency               text not null check (urgency in ('routine','urgent','critical')),
    status                text not null default 'open' check (status in ('open','matched','fulfilled','cancelled','expired','under_review')),
    matched_donor_user_id uuid references "AspNetUsers" ("Id") on delete set null,
    current_radius_km     integer not null default 25 check (current_radius_km between 1 and 200),
    wave_index            integer not null default 0,
    created_at_utc        timestamptz not null default now(),
    updated_at_utc        timestamptz not null default now(),
    expires_at_utc        timestamptz not null
);

create index donation_requests_open_gix on donation_requests using gist (destination)
    where status = 'open';
create index donation_requests_expires_ix on donation_requests (expires_at_utc)
    where status = 'open';
create index donation_requests_requester_ix on donation_requests (requester_id, created_at_utc desc);

-- -----------------------------------------------------------------------------
-- Donor responses (one per donor per request; uniqueness enforced here)
-- -----------------------------------------------------------------------------
create table request_responses (
    id            uuid primary key default gen_random_uuid(),
    request_id    uuid not null references donation_requests (id) on delete cascade,
    donor_user_id uuid not null references "AspNetUsers" ("Id") on delete cascade,
    response      text not null check (response in ('accepted','declined')),
    created_at_utc timestamptz not null default now(),
    constraint request_responses_one_per_donor_uq unique (request_id, donor_user_id)
);

create index request_responses_donor_ix on request_responses (donor_user_id, created_at_utc desc);

-- -----------------------------------------------------------------------------
-- Blood-group compatibility reference data (red-cell compatibility policy)
-- -----------------------------------------------------------------------------
create table blood_group_compatibility (
    recipient_group text not null check (recipient_group in ('A+','A-','B+','B-','AB+','AB-','O+','O-')),
    donor_group     text not null check (donor_group in ('A+','A-','B+','B-','AB+','AB-','O+','O-')),
    is_compatible   boolean not null,
    primary key (recipient_group, donor_group)
);

-- -----------------------------------------------------------------------------
-- Donation records (provenance: who reported what, and its verification status)
-- -----------------------------------------------------------------------------
create table donation_records (
    id                  uuid primary key default gen_random_uuid(),
    request_id          uuid references donation_requests (id) on delete set null,
    donor_user_id       uuid not null references "AspNetUsers" ("Id") on delete cascade,
    reported_by_user_id uuid not null references "AspNetUsers" ("Id") on delete cascade,
    donation_date       date not null check (donation_date <= current_date and donation_date >= current_date - interval '120 days'),
    status              text not null default 'reported'
        check (status in ('reported','awaiting_confirmation','completed_unverified','verified','disputed','rejected','corrected')),
    policy_version_id   uuid references deferral_policy_versions (id),
    eligible_again_at   timestamptz,
    confirmed_by_user_id uuid references "AspNetUsers" ("Id"),
    confirmed_at_utc    timestamptz,
    notes               text check (notes is null or char_length(notes) <= 1000),
    created_at_utc      timestamptz not null default now(),
    updated_at_utc      timestamptz not null default now()
);

-- One donation record per request while the record is not rejected (v1: a
-- request has a single donor slot). Rejected records do not block a corrected
-- re-report.
create unique index donation_records_request_active_uq on donation_records (request_id)
    where status <> 'rejected';
create index donation_records_donor_ix on donation_records (donor_user_id, donation_date desc);
create index donation_records_status_ix on donation_records (status);

-- Append-only history of every change to a donation record.
create table donation_record_events (
    id                 uuid primary key default gen_random_uuid(),
    donation_record_id uuid not null references donation_records (id) on delete cascade,
    event_type         text not null,
    actor_user_id      uuid references "AspNetUsers" ("Id"),
    from_status        text,
    to_status          text,
    details            jsonb not null default '{}',
    created_at_utc     timestamptz not null default now()
);

create index donation_record_events_record_ix on donation_record_events (donation_record_id, created_at_utc);

-- -----------------------------------------------------------------------------
-- Donor deferrals (eligibility restrictions; derived from policy versions)
-- -----------------------------------------------------------------------------
create table donor_deferrals (
    id                 uuid primary key default gen_random_uuid(),
    donor_user_id      uuid not null references "AspNetUsers" ("Id") on delete cascade,
    donation_record_id uuid references donation_records (id) on delete cascade,
    deferral_type      text not null check (deferral_type in ('provisional','verified','administrative')),
    policy_version_id  uuid references deferral_policy_versions (id),
    starts_at_utc      timestamptz not null default now(),
    eligible_again_at  timestamptz not null,
    revoked_at_utc     timestamptz,
    revoked_reason     text,
    created_at_utc     timestamptz not null default now()
);

create index donor_deferrals_active_ix on donor_deferrals (donor_user_id, eligible_again_at)
    where revoked_at_utc is null;

-- -----------------------------------------------------------------------------
-- Matching runs and membership (wave-based donor notification)
-- -----------------------------------------------------------------------------
create table matching_runs (
    id               uuid primary key default gen_random_uuid(),
    request_id       uuid not null references donation_requests (id) on delete cascade,
    wave_index       integer not null check (wave_index >= 0),
    radius_km        integer not null check (radius_km between 1 and 200),
    status           text not null default 'pending' check (status in ('pending','processing','completed','cancelled','failed')),
    due_at_utc       timestamptz not null,
    candidates_found integer,
    notified_count   integer,
    failure_reason   text,
    leased_by        text,
    leased_until     timestamptz,
    created_at_utc   timestamptz not null default now(),
    started_at_utc   timestamptz,
    completed_at_utc timestamptz,
    constraint matching_runs_wave_uq unique (request_id, wave_index)
);

create index matching_runs_due_ix on matching_runs (due_at_utc) where status = 'pending';

create table matching_run_donors (
    matching_run_id uuid not null references matching_runs (id) on delete cascade,
    request_id      uuid not null references donation_requests (id) on delete cascade,
    donor_user_id   uuid not null references "AspNetUsers" ("Id") on delete cascade,
    distance_m      double precision not null check (distance_m >= 0),
    created_at_utc  timestamptz not null default now(),
    primary key (matching_run_id, donor_user_id)
);

-- Denormalized request_id makes "already selected in this request's runs" a
-- cheap indexed anti-join when later waves run.
create index matching_run_donors_request_donor_ix on matching_run_donors (request_id, donor_user_id);

-- -----------------------------------------------------------------------------
-- Notification history (in-app inbox; source of truth for what users were told)
-- -----------------------------------------------------------------------------
create table notification_history (
    id                  uuid primary key default gen_random_uuid(),
    user_id             uuid not null references "AspNetUsers" ("Id") on delete cascade,
    request_id          uuid references donation_requests (id) on delete set null,
    type                text not null,
    title               text not null,
    body                text not null,
    deep_link           text,
    created_at_utc      timestamptz not null default now(),
    read_at_utc         timestamptz,
    delivered_at_utc    timestamptz,
    provider_message_id text
);

create index notification_history_user_ix on notification_history (user_id, created_at_utc desc);
create index notification_history_unread_ix on notification_history (user_id)
    where read_at_utc is null;

-- -----------------------------------------------------------------------------
-- Notification outbox (transactional outbox; delivered by the worker)
-- -----------------------------------------------------------------------------
create table notification_outbox (
    id                uuid primary key default gen_random_uuid(),
    notification_id   uuid not null references notification_history (id) on delete cascade,
    user_id           uuid not null references "AspNetUsers" ("Id") on delete cascade,
    status            text not null default 'pending' check (status in ('pending','processing','delivered','failed','skipped')),
    attempts          integer not null default 0,
    max_attempts      integer not null default 8,
    available_at_utc  timestamptz not null default now(),
    last_attempt_at_utc timestamptz,
    delivered_at_utc  timestamptz,
    last_error        text,
    leased_by         text,
    leased_until      timestamptz,
    created_at_utc    timestamptz not null default now()
);

create index notification_outbox_due_ix on notification_outbox (available_at_utc)
    where status = 'pending';
create index notification_outbox_lease_ix on notification_outbox (leased_until)
    where status = 'processing';

-- -----------------------------------------------------------------------------
-- User devices (OneSignal subscription registry)
-- -----------------------------------------------------------------------------
create table user_devices (
    id                         uuid primary key default gen_random_uuid(),
    user_id                    uuid not null references "AspNetUsers" ("Id") on delete cascade,
    onesignal_subscription_id  text not null unique,
    platform                   text not null check (platform in ('android','ios','web')),
    is_active                  boolean not null default true,
    created_at_utc             timestamptz not null default now(),
    updated_at_utc             timestamptz not null default now(),
    last_registered_at_utc     timestamptz not null default now()
);

create index user_devices_user_ix on user_devices (user_id) where is_active;

-- -----------------------------------------------------------------------------
-- Abuse reports and account suspensions (v1 basic moderation)
-- -----------------------------------------------------------------------------
create table abuse_reports (
    id                 uuid primary key default gen_random_uuid(),
    reported_by_user_id uuid not null references "AspNetUsers" ("Id") on delete cascade,
    target_user_id     uuid references "AspNetUsers" ("Id") on delete set null,
    target_request_id  uuid references donation_requests (id) on delete set null,
    category           text not null check (char_length(category) between 1 and 64),
    details            text not null check (char_length(details) between 1 and 2000),
    status             text not null default 'open' check (status in ('open','reviewing','resolved','dismissed')),
    resolution_note    text check (resolution_note is null or char_length(resolution_note) <= 1000),
    resolved_by_user_id uuid references "AspNetUsers" ("Id"),
    resolved_at_utc    timestamptz,
    created_at_utc     timestamptz not null default now()
);

create index abuse_reports_status_ix on abuse_reports (status, created_at_utc);

create table account_suspensions (
    id                  uuid primary key default gen_random_uuid(),
    user_id             uuid not null references "AspNetUsers" ("Id") on delete cascade,
    suspended_by_user_id uuid not null references "AspNetUsers" ("Id"),
    reason              text not null check (char_length(reason) between 1 and 1000),
    created_at_utc      timestamptz not null default now(),
    lifted_by_user_id   uuid references "AspNetUsers" ("Id"),
    lifted_at_utc       timestamptz,
    lift_reason         text
);

create index account_suspensions_active_ix on account_suspensions (user_id)
    where lifted_at_utc is null;

-- -----------------------------------------------------------------------------
-- Refresh-token sessions (rotating, revocable; see docs/authentication.md)
-- -----------------------------------------------------------------------------
create table auth_refresh_tokens (
    id                   uuid primary key default gen_random_uuid(),
    user_id              uuid not null references "AspNetUsers" ("Id") on delete cascade,
    token_hash           text not null unique,
    created_at_utc       timestamptz not null default now(),
    expires_at_utc       timestamptz not null,
    revoked_at_utc       timestamptz,
    revoked_reason       text,
    replaced_by_token_id uuid references auth_refresh_tokens (id)
);

create index auth_refresh_tokens_user_ix on auth_refresh_tokens (user_id);

-- -----------------------------------------------------------------------------
-- Verification codes (email/phone verification and password recovery; codes are
-- stored hashed. Delivery provider is pluggable — see docs/authentication.md)
-- -----------------------------------------------------------------------------
create table verification_codes (
    id            uuid primary key default gen_random_uuid(),
    user_id       uuid not null references "AspNetUsers" ("Id") on delete cascade,
    purpose       text not null check (purpose in ('email_verification','phone_verification','password_reset')),
    code_hash     text not null,
    expires_at_utc timestamptz not null,
    consumed_at_utc timestamptz,
    attempts      integer not null default 0,
    created_at_utc timestamptz not null default now()
);

create index verification_codes_user_purpose_ix on verification_codes (user_id, purpose, created_at_utc desc);

-- -----------------------------------------------------------------------------
-- Append-only audit log
-- -----------------------------------------------------------------------------
create table audit_log (
    id             uuid primary key default gen_random_uuid(),
    actor_user_id  uuid references "AspNetUsers" ("Id"),
    action         text not null,
    entity_type    text not null,
    entity_id      text,
    details        jsonb not null default '{}',
    correlation_id text,
    created_at_utc timestamptz not null default now()
);

create index audit_log_entity_ix on audit_log (entity_type, entity_id);
create index audit_log_actor_ix on audit_log (actor_user_id, created_at_utc desc);

-- Enforce append-only behaviour at the database level (role-independent).
create or replace function aff_prevent_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'table % is append-only', tg_table_name;
end;
$$;

create trigger audit_log_append_only
    before update or delete on audit_log
    for each row execute function aff_prevent_mutation();

create trigger donation_record_events_append_only
    before update or delete on donation_record_events
    for each row execute function aff_prevent_mutation();
