using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Contracts;
using Dapper;
using Npgsql;
using Domain = AF.Blood.Application.Domain;

namespace AF.Blood.Application.Features.Donors;

public sealed class DonorProfileService(NpgsqlDataSource db, IClock clock, AuditWriter audit)
{
    public async Task<DonorProfileDto> UpsertAsync(
        Guid userId, UpsertDonorProfileRequest request, string? correlationId, CancellationToken ct)
    {
        if (!BloodGroups.IsValid(request.BloodGroup))
        {
            throw AppException.Invalid("Unknown blood group.");
        }

        if (request.SearchRadiusKm is < 1 or > 100)
        {
            throw AppException.Invalid("Search radius must be between 1 and 100 km.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await GetRowAsync(conn, tx, userId, ct);
        if (existing is null)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into donor_profiles (user_id, blood_group, search_radius_km)
                values (@userId, @bloodGroup, @radius)
                """,
                new { userId, bloodGroup = request.BloodGroup, radius = request.SearchRadiusKm },
                tx,
                cancellationToken: ct));
            await audit.WriteAsync(conn, tx, userId, "donor_profile.created", "donor_profile",
                userId.ToString(), new { request.BloodGroup, request.SearchRadiusKm }, correlationId, ct);
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update donor_profiles
                set blood_group = @bloodGroup, search_radius_km = @radius, updated_at_utc = now()
                where user_id = @userId
                """,
                new { userId, bloodGroup = request.BloodGroup, radius = request.SearchRadiusKm },
                tx,
                cancellationToken: ct));
            await audit.WriteAsync(conn, tx, userId, "donor_profile.updated", "donor_profile",
                userId.ToString(), new { request.BloodGroup, request.SearchRadiusKm }, correlationId, ct);
        }

        await tx.CommitAsync(ct);
        return (await GetAsync(userId, ct))!;
    }

    public async Task<DonorProfileDto?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await GetRowAsync(conn, tx: null, userId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<DonorProfileDto> SetConsentAsync(
        Guid userId, DonorConsentRequest request, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConsentVersion))
        {
            throw AppException.Invalid("Consent version is required.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var profile = await GetRowAsync(conn, tx, userId, ct)
            ?? throw AppException.NotFound("Create a donor profile first.");

        if (request.Accept)
        {
            if (request.Latitude is null || request.Longitude is null)
            {
                throw AppException.Invalid("An approximate location is required to enable discovery.");
            }

            var (lat, lon) = Domain.GeoGeneralizer.Generalize(request.Latitude.Value, request.Longitude.Value);

            await conn.ExecuteAsync(new CommandDefinition("""
                update donor_profiles
                set is_discoverable = true,
                    consent_version = @consentVersion,
                    consented_at_utc = now(),
                    location = st_setsrid(st_makepoint(@lon, @lat), 4326)::geography,
                    updated_at_utc = now()
                where user_id = @userId
                """,
                new { userId, consentVersion = request.ConsentVersion.Trim(), lat, lon },
                tx,
                cancellationToken: ct));

            await audit.WriteAsync(conn, tx, userId, "donor_profile.consent_granted", "donor_profile",
                userId.ToString(), new { request.ConsentVersion }, correlationId, ct);
        }
        else
        {
            // Withdrawal: discovery stops immediately; the stored location is
            // removed as well so opting out is a genuine data-minimisation act.
            await conn.ExecuteAsync(new CommandDefinition("""
                update donor_profiles
                set is_discoverable = false,
                    is_available = false,
                    consent_version = null,
                    consented_at_utc = null,
                    location = null,
                    updated_at_utc = now()
                where user_id = @userId
                """,
                new { userId },
                tx,
                cancellationToken: ct));

            await audit.WriteAsync(conn, tx, userId, "donor_profile.consent_withdrawn", "donor_profile",
                userId.ToString(), new { }, correlationId, ct);
        }

        await tx.CommitAsync(ct);
        return (await GetAsync(userId, ct))!;
    }

    public async Task<DonorProfileDto> SetAvailabilityAsync(
        Guid userId, DonorAvailabilityRequest request, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        _ = await GetRowAsync(conn, tx, userId, ct)
            ?? throw AppException.NotFound("Create a donor profile first.");

        await conn.ExecuteAsync(new CommandDefinition("""
            update donor_profiles set is_available = @isAvailable, updated_at_utc = now() where user_id = @userId
            """,
            new { userId, request.IsAvailable },
            tx,
            cancellationToken: ct));

        await audit.WriteAsync(conn, tx, userId, "donor_profile.availability_changed", "donor_profile",
            userId.ToString(), new { request.IsAvailable }, correlationId, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(userId, ct))!;
    }

    public async Task<DonorEligibilityDto> GetEligibilityAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<EligibilityRow>(new CommandDefinition("""
            select dp.is_discoverable,
                   dp.is_available,
                   d.eligible_again_at,
                   d.deferral_type,
                   p.version as policy_version,
                   (select max(d2.eligible_again_at)
                      from donor_deferrals d2
                     where d2.donor_user_id = dp.user_id) as last_deferral_until
              from donor_profiles dp
              left join lateral (
                    select dd.eligible_again_at, dd.deferral_type, dd.policy_version_id
                      from donor_deferrals dd
                     where dd.donor_user_id = dp.user_id
                       and dd.revoked_at_utc is null
                       and dd.eligible_again_at > now()
                     order by dd.eligible_again_at desc
                     limit 1) d on true
              left join deferral_policy_versions p on p.id = d.policy_version_id
             where dp.user_id = @userId
            """,
            new { userId },
            cancellationToken: ct));

        if (row is null)
        {
            throw AppException.NotFound("No donor profile found.");
        }

        var deferred = row.Eligible_again_at is not null;
        var remainingDays = deferred
            ? (int?)Math.Ceiling((row.Eligible_again_at!.Value - clock.UtcNow).TotalDays)
            : null;

        string message;
        if (deferred)
        {
            message = row.Deferral_type == DeferralTypes.Provisional
                ? "A provisional restriction is active because a donation report has not been confirmed yet."
                : "You are temporarily not shown to new matching requests.";
        }
        else if (row.Last_deferral_until is not null)
        {
            // Mandated wording (docs/donation-reporting-and-deferrals.md):
            // countdown expiry is not medical clearance.
            message = "Your recorded deferral period has ended. Final eligibility is determined by the donation centre.";
        }
        else
        {
            message = "No active deferral is recorded. Final eligibility for any donation is determined by the donation centre.";
        }

        return new DonorEligibilityDto(
            row.Is_discoverable,
            row.Is_available,
            deferred,
            row.Deferral_type,
            row.Eligible_again_at,
            row.Policy_version,
            remainingDays,
            message);
    }

    private static async Task<ProfileRow?> GetRowAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid userId, CancellationToken ct)
    {
        return await conn.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition("""
            select user_id, blood_group, is_discoverable, is_available, search_radius_km,
                   consent_version, consented_at_utc, created_at_utc, updated_at_utc,
                   case when location is null then null else st_y(location::geometry) end as lat,
                   case when location is null then null else st_x(location::geometry) end as lon
              from donor_profiles
             where user_id = @userId
            """,
            new { userId },
            tx,
            cancellationToken: ct));
    }

    private static DonorProfileDto Map(ProfileRow r) => new(
        r.User_id, r.Blood_group, r.Is_discoverable, r.Is_available, r.Lat, r.Lon,
        r.Search_radius_km, r.Consent_version, r.Consented_at_utc, r.Created_at_utc, r.Updated_at_utc);

    private sealed class ProfileRow
    {
        public Guid User_id { get; init; }
        public string Blood_group { get; init; } = null!;
        public bool Is_discoverable { get; init; }
        public bool Is_available { get; init; }
        public int Search_radius_km { get; init; }
        public string? Consent_version { get; init; }
        public DateTimeOffset? Consented_at_utc { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
        public DateTimeOffset Updated_at_utc { get; init; }
        public double? Lat { get; init; }
        public double? Lon { get; init; }
    }

    private sealed class EligibilityRow
    {
        public bool Is_discoverable { get; init; }
        public bool Is_available { get; init; }
        public DateTimeOffset? Eligible_again_at { get; init; }
        public string? Deferral_type { get; init; }
        public string? Policy_version { get; init; }
        public DateTimeOffset? Last_deferral_until { get; init; }
    }
}
