using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Matching;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Application.Options;
using AF.Blood.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Domain = AF.Blood.Application.Domain;

namespace AF.Blood.Application.Features.Requests;

public sealed class RequestService(
    NpgsqlDataSource db,
    IClock clock,
    AuditWriter audit,
    OutboxEnqueuer outbox,
    IOptions<RequestOptions> requestOptions,
    IOptions<MatchingOptions> matchingOptions)
{
    private readonly RequestOptions _requestOptions = requestOptions.Value;
    private readonly MatchingOptions _matchingOptions = matchingOptions.Value;

    public async Task<RequestDto> CreateAsync(
        Guid requesterId, CreateRequestRequest request, string? correlationId, CancellationToken ct)
    {
        if (!Domain.BloodGroups.IsValid(request.BloodGroup))
        {
            throw AppException.Invalid("Unknown blood group.");
        }

        if (!Urgencies.IsValid(request.Urgency))
        {
            throw AppException.Invalid("Unknown urgency.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationLabel) || request.DestinationLabel.Length > 200)
        {
            throw AppException.Invalid("Destination label is required (max 200 characters).");
        }

        if (request.ReferenceNote is { Length: > 500 })
        {
            throw AppException.Invalid("Reference note must be at most 500 characters.");
        }

        var (lat, lon) = Domain.GeoGeneralizer.Generalize(request.Latitude, request.Longitude);

        var expiryHours = request.ExpiresInHours ?? _requestOptions.DefaultExpiryHours;
        if (expiryHours < _requestOptions.MinExpiryHours || expiryHours > _requestOptions.MaxExpiryHours)
        {
            throw AppException.Invalid(
                $"Expiry must be between {_requestOptions.MinExpiryHours} and {_requestOptions.MaxExpiryHours} hours.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var openCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*) from donation_requests
             where requester_id = @requesterId and status = 'open'
            """,
            new { requesterId },
            tx,
            cancellationToken: ct));

        if (openCount >= _requestOptions.MaxOpenRequestsPerUser)
        {
            throw AppException.Conflict(
                $"You already have {_requestOptions.MaxOpenRequestsPerUser} open requests. Cancel one before creating another.");
        }

        var initialRadius = _matchingOptions.RadiiKm.Length > 0 ? _matchingOptions.RadiiKm[0] : 25;

        var requestId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into donation_requests
                (requester_id, blood_group, destination, destination_label, facility_name,
                 reference_note, urgency, current_radius_km, expires_at_utc)
            values
                (@requesterId, @bloodGroup, st_setsrid(st_makepoint(@lon, @lat), 4326)::geography,
                 @destinationLabel, @facilityName, @referenceNote, @urgency, @initialRadius,
                 now() + make_interval(hours => @expiryHours))
            returning id
            """,
            new
            {
                requesterId,
                bloodGroup = request.BloodGroup,
                lat,
                lon,
                destinationLabel = request.DestinationLabel.Trim(),
                facilityName = string.IsNullOrWhiteSpace(request.FacilityName) ? null : request.FacilityName.Trim(),
                referenceNote = string.IsNullOrWhiteSpace(request.ReferenceNote) ? null : request.ReferenceNote.Trim(),
                urgency = request.Urgency,
                initialRadius,
                expiryHours,
            },
            tx,
            cancellationToken: ct));

        await MatchingService.CreateRunAsync(conn, tx, requestId, waveIndex: 0, initialRadius, clock.UtcNow, ct);

        await audit.WriteAsync(conn, tx, requesterId, "request.created", "donation_request",
            requestId.ToString(), new { request.BloodGroup, request.Urgency, expiryHours }, correlationId, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(requestId, requesterId, ct))!;
    }

    /// <summary>Accepts or declines a request. Acceptance is serialised on the
    /// request row (SELECT … FOR UPDATE) so that concurrent accepters cannot both
    /// take the single donor slot; the loser receives a conflict.</summary>
    public async Task RespondAsync(
        Guid donorUserId, Guid requestId, string response, string? correlationId, CancellationToken ct)
    {
        if (!ResponseTypes.IsValid(response))
        {
            throw AppException.Invalid("Response must be 'accepted' or 'declined'.");
        }

        var accepting = response == ResponseTypes.Accepted;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var request = await conn.QuerySingleOrDefaultAsync<RequestLockRow>(new CommandDefinition("""
            select id, requester_id, status, expires_at_utc, matched_donor_user_id
              from donation_requests
             where id = @requestId
             for update
            """,
            new { requestId },
            tx,
            cancellationToken: ct));

        if (request is null)
        {
            throw AppException.NotFound("Request not found.");
        }

        if (request.Requester_id == donorUserId)
        {
            throw AppException.Invalid("You cannot respond to your own request.");
        }

        if (!Domain.RequestStateMachine.AcceptsResponses(request.Status, request.Expires_at_utc, clock.UtcNow))
        {
            throw AppException.Conflict("This request is no longer open for responses.");
        }

        if (accepting && request.Matched_donor_user_id is not null)
        {
            throw AppException.Conflict("Another donor has already accepted this request.");
        }

        if (accepting)
        {
            await EnsureDonorEligibleToAcceptAsync(conn, tx, donorUserId, ct);
        }

        var inserted = await conn.ExecuteAsync(new CommandDefinition("""
            insert into request_responses (request_id, donor_user_id, response)
            values (@requestId, @donorUserId, @response)
            on conflict (request_id, donor_user_id) do nothing
            """,
            new { requestId, donorUserId, response },
            tx,
            cancellationToken: ct));

        if (inserted == 0)
        {
            throw AppException.Conflict("You have already responded to this request.");
        }

        if (accepting)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update donation_requests
                   set status = @status, matched_donor_user_id = @donorUserId, updated_at_utc = now()
                 where id = @requestId
                """,
                new { status = Domain.RequestStateMachine.Matched, donorUserId, requestId },
                tx,
                cancellationToken: ct));
        }

        if (accepting)
        {
            var donorName = await conn.ExecuteScalarAsync<string>(new CommandDefinition("""
                select "DisplayName" from "AspNetUsers" where "Id" = @donorUserId
                """,
                new { donorUserId },
                tx,
                cancellationToken: ct));

            await outbox.EnqueueAsync(conn, tx, request.Requester_id, requestId,
                NotificationTypes.DonorAccepted,
                "A donor accepted your request",
                $"{donorName ?? "A donor"} accepted to help. Coordinate the donation in the app.",
                $"affoundation://requests/{requestId}", ct);
        }

        await audit.WriteAsync(conn, tx, donorUserId, accepting ? "request.accepted" : "request.declined",
            "donation_request", requestId.ToString(), new { response }, correlationId, ct);

        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid userId, Guid requestId, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var request = await conn.QuerySingleOrDefaultAsync<RequestLockRow>(new CommandDefinition("""
            select id, requester_id, status, expires_at_utc, matched_donor_user_id
              from donation_requests
             where id = @requestId
             for update
            """,
            new { requestId },
            tx,
            cancellationToken: ct));

        if (request is null)
        {
            throw AppException.NotFound("Request not found.");
        }

        if (request.Requester_id != userId)
        {
            throw AppException.Forbidden("Only the request owner can cancel it.");
        }

        if (request.Status is Domain.RequestStateMachine.Cancelled or Domain.RequestStateMachine.Expired or Domain.RequestStateMachine.Fulfilled)
        {
            throw AppException.Conflict($"Request is already {request.Status}.");
        }

        await conn.ExecuteAsync(new CommandDefinition("""
            update donation_requests set status = @status, updated_at_utc = now() where id = @requestId
            """,
            new { status = Domain.RequestStateMachine.Cancelled, requestId },
            tx,
            cancellationToken: ct));

        await MatchingService.CancelPendingRunsAsync(conn, tx, requestId, ct);

        if (request.Matched_donor_user_id is not null)
        {
            await outbox.EnqueueAsync(conn, tx, request.Matched_donor_user_id.Value, requestId,
                NotificationTypes.RequestCancelled,
                "A request you accepted was cancelled",
                "The requester cancelled the blood request you accepted.",
                $"affoundation://requests/{requestId}", ct);
        }

        await audit.WriteAsync(conn, tx, userId, "request.cancelled", "donation_request",
            requestId.ToString(), new { }, correlationId, ct);

        await tx.CommitAsync(ct);
    }

    public async Task<RequestDto?> GetAsync(Guid requestId, Guid viewerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RequestDetailRow>(new CommandDefinition("""
            select r.id, r.requester_id, ru."DisplayName" as requester_name,
                   r.blood_group, r.component_type, r.destination_label, r.facility_name,
                   r.reference_note, r.urgency, r.status, r.created_at_utc, r.expires_at_utc,
                   r.matched_donor_user_id, du."DisplayName" as matched_donor_name,
                   st_y(r.destination::geometry) as lat, st_x(r.destination::geometry) as lon,
                   (select count(*) from request_responses rr
                     where rr.request_id = r.id and rr.response = 'accepted') as accepted_count,
                   (select rr2.response from request_responses rr2
                     where rr2.request_id = r.id and rr2.donor_user_id = @viewerId) as my_response
              from donation_requests r
              join "AspNetUsers" ru on ru."Id" = r.requester_id
              left join "AspNetUsers" du on du."Id" = r.matched_donor_user_id
             where r.id = @requestId
            """,
            new { requestId, viewerId },
            cancellationToken: ct));

        if (row is null)
        {
            return null;
        }

        return MapDetail(row);
    }

    public async Task<PagedResult<RequestDto>> ListMineAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RequestDetailRow>(new CommandDefinition("""
            select r.id, r.requester_id, ru."DisplayName" as requester_name,
                   r.blood_group, r.component_type, r.destination_label, r.facility_name,
                   r.reference_note, r.urgency, r.status, r.created_at_utc, r.expires_at_utc,
                   r.matched_donor_user_id, du."DisplayName" as matched_donor_name,
                   st_y(r.destination::geometry) as lat, st_x(r.destination::geometry) as lon,
                   (select count(*) from request_responses rr
                     where rr.request_id = r.id and rr.response = 'accepted') as accepted_count,
                   (select rr2.response from request_responses rr2
                     where rr2.request_id = r.id and rr2.donor_user_id = @userId) as my_response
              from donation_requests r
              join "AspNetUsers" ru on ru."Id" = r.requester_id
              left join "AspNetUsers" du on du."Id" = r.matched_donor_user_id
             where r.requester_id = @userId
             order by r.created_at_utc desc
             limit @limit offset @offset
            """,
            new { userId, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from donation_requests where requester_id = @userId",
            new { userId }, cancellationToken: ct));

        return new PagedResult<RequestDto>(rows.Select(MapDetail).ToList(), total, page, pageSize);
    }

    public async Task<PagedResult<NearbyRequestDto>> BrowseNearbyAsync(
        Guid donorUserId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var profile = await conn.QuerySingleOrDefaultAsync<(double? Lat, double? Lon, int Radius, string BloodGroup)>(
            new CommandDefinition("""
                select case when location is null then null else st_y(location::geometry) end as lat,
                       case when location is null then null else st_x(location::geometry) end as lon,
                       search_radius_km as radius, blood_group
                  from donor_profiles where user_id = @donorUserId
                """,
                new { donorUserId },
                cancellationToken: ct));

        if (profile.BloodGroup is null)
        {
            throw AppException.Invalid("Create a donor profile before browsing requests.");
        }

        if (profile.Lat is null || profile.Lon is null)
        {
            throw AppException.Invalid("Set your approximate location before browsing nearby requests.");
        }

        // Requests this donor may help: the requested group can receive the donor's group.
        var compatible = Domain.BloodCompatibility.CompatibleDonorGroupsForDonor(profile.BloodGroup);

        var rows = await conn.QueryAsync<NearbyRow>(new CommandDefinition("""
            select r.id, r.blood_group, r.urgency, r.status, r.destination_label,
                   st_y(r.destination::geometry) as lat, st_x(r.destination::geometry) as lon,
                   st_distance(r.destination, dp.location) as distance_m,
                   r.created_at_utc, r.expires_at_utc
              from donation_requests r
              join donor_profiles dp on dp.user_id = @donorUserId
             where r.status = 'open'
               and r.expires_at_utc > now()
               and r.requester_id <> @donorUserId
               and r.blood_group = any(@compatible)
               and st_dwithin(r.destination, dp.location, @radiusMetres)
             order by st_distance(r.destination, dp.location) asc, r.created_at_utc asc
             limit @limit offset @offset
            """,
            new { donorUserId, compatible, radiusMetres = profile.Radius * 1000.0, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*)
              from donation_requests r
              join donor_profiles dp on dp.user_id = @donorUserId
             where r.status = 'open'
               and r.expires_at_utc > now()
               and r.requester_id <> @donorUserId
               and r.blood_group = any(@compatible)
               and st_dwithin(r.destination, dp.location, @radiusMetres)
            """,
            new { donorUserId, compatible, radiusMetres = profile.Radius * 1000.0 },
            cancellationToken: ct));

        var items = rows.Select(r => new NearbyRequestDto(
            r.Id, r.Blood_group, r.Urgency, r.Status, r.Destination_label,
            r.Lat, r.Lon, Math.Round(r.Distance_m / 1000.0, 1),
            r.Created_at_utc, r.Expires_at_utc)).ToList();

        return new PagedResult<NearbyRequestDto>(items, total, page, pageSize);
    }

    public async Task<PagedResult<ResponseDto>> ListMyResponsesAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ResponseRow>(new CommandDefinition("""
            select rr.id, rr.request_id, rr.donor_user_id, u."DisplayName" as donor_name,
                   rr.response, rr.created_at_utc
              from request_responses rr
              join "AspNetUsers" u on u."Id" = rr.donor_user_id
             where rr.donor_user_id = @userId
             order by rr.created_at_utc desc
             limit @limit offset @offset
            """,
            new { userId, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from request_responses where donor_user_id = @userId",
            new { userId }, cancellationToken: ct));

        var items = rows.Select(r => new ResponseDto(r.Id, r.Request_id, r.Donor_user_id, r.Donor_name, r.Response, r.Created_at_utc)).ToList();
        return new PagedResult<ResponseDto>(items, total, page, pageSize);
    }

    public async Task<PagedResult<ResponseDto>> ListResponsesAsync(Guid requestId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ResponseRow>(new CommandDefinition("""
            select rr.id, rr.request_id, rr.donor_user_id, u."DisplayName" as donor_name,
                   rr.response, rr.created_at_utc
              from request_responses rr
              join "AspNetUsers" u on u."Id" = rr.donor_user_id
             where rr.request_id = @requestId
             order by rr.created_at_utc asc
             limit @limit offset @offset
            """,
            new { requestId, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from request_responses where request_id = @requestId",
            new { requestId }, cancellationToken: ct));

        var items = rows.Select(r => new ResponseDto(r.Id, r.Request_id, r.Donor_user_id, r.Donor_name, r.Response, r.Created_at_utc)).ToList();
        return new PagedResult<ResponseDto>(items, total, page, pageSize);
    }

    /// <summary>Revalidates, at acceptance time, everything the candidate query
    /// checked earlier — candidates can go stale between a wave and the tap.</summary>
    private static async Task EnsureDonorEligibleToAcceptAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid donorUserId, CancellationToken ct)
    {
        var profile = await conn.QuerySingleOrDefaultAsync<(bool Discoverable, bool Available)>(
            new CommandDefinition("""
                select is_discoverable as discoverable, is_available as available
                  from donor_profiles where user_id = @donorUserId
                """,
                new { donorUserId }, tx, cancellationToken: ct));

        if (profile == default)
        {
            throw AppException.Invalid("Create a donor profile before accepting requests.");
        }

        if (!profile.Discoverable || !profile.Available)
        {
            throw AppException.Conflict(
                "Enable discovery consent and mark yourself available before accepting requests.");
        }

        var blocked = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (
                     select 1 from donor_deferrals
                      where donor_user_id = @donorUserId
                        and revoked_at_utc is null
                        and eligible_again_at > now())
                 or exists (
                     select 1 from account_suspensions
                      where user_id = @donorUserId and lifted_at_utc is null)
            """,
            new { donorUserId }, tx, cancellationToken: ct));

        if (blocked)
        {
            throw AppException.Conflict(
                "You cannot accept requests while an eligibility restriction or account suspension is active.");
        }
    }

    private static RequestDto MapDetail(RequestDetailRow r) => new(
        r.Id, r.Requester_id, r.Requester_name, r.Blood_group, r.Component_type,
        r.Destination_label, r.Lat, r.Lon, r.Facility_name, r.Reference_note,
        r.Urgency, r.Status, r.Created_at_utc, r.Expires_at_utc,
        r.Matched_donor_user_id, r.Matched_donor_name, null,
        r.Accepted_count, r.My_response == ResponseTypes.Accepted);

    private sealed class RequestLockRow
    {
        public Guid Id { get; init; }
        public Guid Requester_id { get; init; }
        public string Status { get; init; } = null!;
        public DateTimeOffset Expires_at_utc { get; init; }
        public Guid? Matched_donor_user_id { get; init; }
    }

    private sealed class RequestDetailRow
    {
        public Guid Id { get; init; }
        public Guid Requester_id { get; init; }
        public string Requester_name { get; init; } = null!;
        public string Blood_group { get; init; } = null!;
        public string Component_type { get; init; } = null!;
        public string Destination_label { get; init; } = null!;
        public string? Facility_name { get; init; }
        public string? Reference_note { get; init; }
        public string Urgency { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateTimeOffset Created_at_utc { get; init; }
        public DateTimeOffset Expires_at_utc { get; init; }
        public Guid? Matched_donor_user_id { get; init; }
        public string? Matched_donor_name { get; init; }
        public double Lat { get; init; }
        public double Lon { get; init; }
        public int Accepted_count { get; init; }
        public string? My_response { get; init; }
    }

    private sealed class NearbyRow
    {
        public Guid Id { get; init; }
        public string Blood_group { get; init; } = null!;
        public string Urgency { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string Destination_label { get; init; } = null!;
        public double Lat { get; init; }
        public double Lon { get; init; }
        public double Distance_m { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
        public DateTimeOffset Expires_at_utc { get; init; }
    }

    private sealed class ResponseRow
    {
        public Guid Id { get; init; }
        public Guid Request_id { get; init; }
        public Guid Donor_user_id { get; init; }
        public string Donor_name { get; init; } = null!;
        public string Response { get; init; } = null!;
        public DateTimeOffset Created_at_utc { get; init; }
    }
}
