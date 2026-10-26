using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Application.Options;
using AF.Blood.Contracts;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AF.Blood.Application.Features.Donations;

/// <summary>
/// Donation outcome reporting and deferral application.
///
/// Safety rules enforced here (docs/donation-reporting-and-deferrals.md):
///  - Acceptance never starts a deferral; only a reported donation does.
///  - Provenance is preserved: self-reported ≠ verified.
///  - A provisional restriction for unverified reports is a configurable
///    policy decision, never hard-coded.
///  - Deferral rows are revoked/replaced with an audit trail; records are
///    never silently overwritten (append-only events).
///  - Countdown expiry is never medical clearance.
/// </summary>
public sealed class DonationService(
    NpgsqlDataSource db,
    IClock clock,
    AuditWriter audit,
    OutboxEnqueuer outbox,
    PolicyService policies,
    IOptions<DonationOptions> donationOptions)
{
    private readonly DonationOptions _options = donationOptions.Value;

    // ------------------------------------------------------------------ report

    public async Task<DonationRecordDto> ReportAsync(
        Guid donorUserId, Guid requestId, DonationReportRequest request, string? correlationId, CancellationToken ct)
    {
        if (request.DonationDate > clock.TodayUtc)
        {
            throw AppException.Invalid("Donation date cannot be in the future.");
        }

        if (request.DonationDate < clock.TodayUtc.AddDays(-_options.MaxReportAgeDays))
        {
            throw AppException.Invalid($"Donation date cannot be older than {_options.MaxReportAgeDays} days.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var req = await conn.QuerySingleOrDefaultAsync<RequestLockRow>(new CommandDefinition("""
            select id, requester_id, status, matched_donor_user_id
              from donation_requests
             where id = @requestId
             for update
            """,
            new { requestId }, tx, cancellationToken: ct))
            ?? throw AppException.NotFound("Request not found.");

        if (req.Matched_donor_user_id != donorUserId)
        {
            throw AppException.Forbidden("Only the donor who accepted this request can report its outcome.");
        }

        if (!RequestStateMachine.AcceptsDonationReport(req.Status))
        {
            throw AppException.Conflict($"A donation cannot be reported while the request is {req.Status}.");
        }

        var existing = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select id from donation_records where request_id = @requestId and status <> 'rejected'
            """,
            new { requestId }, tx, cancellationToken: ct));

        if (existing is not null)
        {
            throw AppException.Conflict("A donation has already been reported for this request.");
        }

        var policy = await policies.GetActiveAsync(conn, tx, ct: ct);

        var provisionalUntil = policy is null
            ? null
            : DeferralCalculator.ProvisionalEligibleAgain(policy, request.DonationDate);

        var recordId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into donation_records
                (request_id, donor_user_id, reported_by_user_id, donation_date, status,
                 policy_version_id, eligible_again_at, notes)
            values (@requestId, @donorUserId, @donorUserId, @donationDate, @status,
                    @policyVersionId, @eligibleAgainAt, @notes)
            returning id
            """,
            new
            {
                requestId,
                donorUserId,
                donationDate = request.DonationDate,
                status = DonationStateMachine.Reported,
                policyVersionId = policy?.Id,
                eligibleAgainAt = provisionalUntil,
                notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            },
            tx,
            cancellationToken: ct));

        // Provisional restriction: an explicit policy decision for self-reported,
        // unverified donations (distinct from the verified-donation deferral).
        if (policy is not null && provisionalUntil is not null)
        {
            await InsertDeferralAsync(conn, tx, donorUserId, recordId, DeferralTypes.Provisional,
                policy.Id, provisionalUntil.Value, ct);
        }

        if (req.Status == RequestStateMachine.Matched)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update donation_requests set status = @status, updated_at_utc = now() where id = @requestId
                """,
                new { status = RequestStateMachine.Fulfilled, requestId }, tx, cancellationToken: ct));
        }

        await InsertEventAsync(conn, tx, recordId, "reported", donorUserId,
            null, DonationStateMachine.Reported, new { request.DonationDate, policy = policy?.Version }, ct);

        await outbox.EnqueueAsync(conn, tx, req.Requester_id, requestId,
            NotificationTypes.DonationReported,
            "Donation reported — please confirm",
            "The donor reported completing the donation for your request. Please confirm or dispute this in the app.",
            $"affoundation://requests/{requestId}", ct);

        await audit.WriteAsync(conn, tx, donorUserId, "donation.reported", "donation_record",
            recordId.ToString(), new { requestId, request.DonationDate, policy = policy?.Version }, correlationId, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(recordId, ct)
            ?? throw new InvalidOperationException("Donation record vanished after insert.");
    }

    // ------------------------------------------------------------ acknowledge

    /// <summary>Requester acknowledges (confirm) or disputes the donor's report.</summary>
    public async Task<DonationRecordDto> AcknowledgeAsync(
        Guid requesterUserId, Guid requestId, AcknowledgementRequest request, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var req = await conn.QuerySingleOrDefaultAsync<RequestLockRow>(new CommandDefinition("""
            select id, requester_id, status, matched_donor_user_id
              from donation_requests
             where id = @requestId
             for update
            """,
            new { requestId }, tx, cancellationToken: ct))
            ?? throw AppException.NotFound("Request not found.");

        if (req.Requester_id != requesterUserId)
        {
            throw AppException.Forbidden("Only the request owner can acknowledge the donation report.");
        }

        var record = await LoadActiveRecordByRequestAsync(conn, tx, requestId, ct)
            ?? throw AppException.NotFound("No donation report exists for this request.");

        if (record.Status != DonationStateMachine.Reported)
        {
            throw AppException.Conflict($"This report is already {record.Status}.");
        }

        if (request.Acknowledge)
        {
            await SetStatusAsync(conn, tx, record.Id, DonationStateMachine.CompletedUnverified,
                requesterUserId, ct);
            await InsertEventAsync(conn, tx, record.Id, "acknowledged", requesterUserId,
                record.Status, DonationStateMachine.CompletedUnverified, new { request.Note }, ct);
            await outbox.EnqueueAsync(conn, tx, record.Donor_user_id, requestId,
                NotificationTypes.DonationAcknowledged,
                "Donation acknowledged",
                "The requester confirmed your donation report. Final eligibility is determined by the donation centre.",
                $"affoundation://requests/{requestId}", ct);
        }
        else
        {
            await SetStatusAsync(conn, tx, record.Id, DonationStateMachine.Disputed, null, ct);
            await InsertEventAsync(conn, tx, record.Id, "disputed", requesterUserId,
                record.Status, DonationStateMachine.Disputed, new { request.Note }, ct);

            if (req.Status is RequestStateMachine.Matched or RequestStateMachine.Fulfilled)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
                    update donation_requests set status = @status, updated_at_utc = now() where id = @requestId
                    """,
                    new { status = RequestStateMachine.UnderReview, requestId }, tx, cancellationToken: ct));
            }

            await NotifyAdministratorsAsync(conn, tx, requestId,
                NotificationTypes.DonationDisputed,
                "Donation report disputed",
                "A requester disputed a donation report. Please review it in the admin console.",
                ct);
        }

        await audit.WriteAsync(conn, tx, requesterUserId, request.Acknowledge ? "donation.acknowledged" : "donation.disputed",
            "donation_record", record.Id.ToString(), new { requestId, request.Note }, correlationId, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(record.Id, ct)!;
    }

    // ------------------------------------------------------------ moderation

    public async Task<DonationRecordDto> VerifyAsync(
        Guid verifierUserId, Guid recordId, string? note, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var record = await LoadRecordForUpdateAsync(conn, tx, recordId, ct);
        RequireTransition(record, DonationStateMachine.Verified, DonationStateMachine.Reported,
            DonationStateMachine.CompletedUnverified, DonationStateMachine.Disputed, DonationStateMachine.Corrected);

        var policy = record.Policy_version_id is not null
            ? await policies.GetByIdAsync(conn, tx, record.Policy_version_id.Value, ct)
            : await policies.GetActiveAsync(conn, tx, ct: ct);

        var eligibleAgain = policy is null ? (DateTimeOffset?)null : DeferralCalculator.EligibleAgainVerified(policy, record.Donation_date);

        await SetStatusAsync(conn, tx, record.Id, DonationStateMachine.Verified, verifierUserId, ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "update donation_records set eligible_again_at = @eligibleAgain where id = @recordId",
            new { eligibleAgain, recordId }, tx, cancellationToken: ct));

        if (policy is not null && eligibleAgain is not null)
        {
            await RevokeDeferralsForRecordAsync(conn, tx, record.Id, "superseded by verified deferral", ct);
            await InsertDeferralAsync(conn, tx, record.Donor_user_id, record.Id, DeferralTypes.Verified,
                policy.Id, eligibleAgain.Value, ct);
        }

        var requestStatus = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "select status from donation_requests where id = @requestId for update",
            new { requestId = record.Request_id }, tx, cancellationToken: ct));

        if (requestStatus == RequestStateMachine.UnderReview && record.Request_id is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update donation_requests set status = @status, updated_at_utc = now() where id = @requestId
                """,
                new { status = RequestStateMachine.Fulfilled, requestId = record.Request_id },
                tx, cancellationToken: ct));
        }

        await InsertEventAsync(conn, tx, record.Id, "verified", verifierUserId, record.Status,
            DonationStateMachine.Verified, new { note, eligibleAgain }, ct);

        if (eligibleAgain is not null)
        {
            await outbox.EnqueueAsync(conn, tx, record.Donor_user_id, record.Request_id,
                NotificationTypes.DonationVerified,
                "Donation verified",
                $"Your donation was verified. You are deferred until {eligibleAgain:yyyy-MM-dd} (UTC). " +
                "Final eligibility is determined by the donation centre.",
                record.Request_id is null ? null : $"affoundation://requests/{record.Request_id}", ct);
        }

        await audit.WriteAsync(conn, tx, verifierUserId, "donation.verified", "donation_record",
            record.Id.ToString(), new { note, eligibleAgain }, correlationId, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(record.Id, ct)!;
    }

    public async Task<DonationRecordDto> RejectAsync(
        Guid verifierUserId, Guid recordId, string? note, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var record = await LoadRecordForUpdateAsync(conn, tx, recordId, ct);
        RequireTransition(record, DonationStateMachine.Rejected, DonationStateMachine.Reported,
            DonationStateMachine.CompletedUnverified, DonationStateMachine.Disputed);

        await SetStatusAsync(conn, tx, record.Id, DonationStateMachine.Rejected, verifierUserId, ct);
        await RevokeDeferralsForRecordAsync(conn, tx, record.Id, "donation report rejected", ct);

        if (record.Request_id is not null)
        {
            var requestStatus = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                "select status from donation_requests where id = @requestId for update",
                new { requestId = record.Request_id }, tx, cancellationToken: ct));

            if (requestStatus == RequestStateMachine.UnderReview)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
                    update donation_requests set status = @status, updated_at_utc = now() where id = @requestId
                    """,
                    new { status = RequestStateMachine.Cancelled, requestId = record.Request_id },
                    tx, cancellationToken: ct));
            }
        }

        await InsertEventAsync(conn, tx, record.Id, "rejected", verifierUserId, record.Status,
            DonationStateMachine.Rejected, new { note }, ct);

        await outbox.EnqueueAsync(conn, tx, record.Donor_user_id, record.Request_id,
            NotificationTypes.DonationRejected,
            "Donation report rejected",
            "Your donation report was reviewed and could not be confirmed. Any restriction from it has been removed.",
            record.Request_id is null ? null : $"affoundation://requests/{record.Request_id}", ct);

        if (record.Request_id is not null)
        {
            var requesterId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select requester_id from donation_requests where id = @requestId",
                new { requestId = record.Request_id }, tx, cancellationToken: ct));
            if (requesterId is not null)
            {
                await outbox.EnqueueAsync(conn, tx, requesterId.Value, record.Request_id,
                    NotificationTypes.DonationRejected,
                    "Donation report rejected",
                    "The donation report for your request was reviewed and rejected.",
                    $"affoundation://requests/{record.Request_id}", ct);
            }
        }

        await audit.WriteAsync(conn, tx, verifierUserId, "donation.rejected", "donation_record",
            record.Id.ToString(), new { note }, correlationId, ct);

        await tx.CommitAsync(ct);
        return await GetAsync(record.Id, ct)!;
    }

    /// <summary>Administrative correction: amends the donation date (recomputing
    /// deferrals from the same policy) and records a 'corrected' event. Values are
    /// never silently overwritten.</summary>
    public async Task<DonationRecordDto> CorrectAsync(
        Guid adminUserId, Guid recordId, DateOnly? correctedDonationDate, string? note,
        string? correlationId, CancellationToken ct)
    {
        if (correctedDonationDate is null && string.IsNullOrWhiteSpace(note))
        {
            throw AppException.Invalid("Provide a corrected donation date or a note.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var record = await LoadRecordForUpdateAsync(conn, tx, recordId, ct)
            ?? throw AppException.NotFound("Donation record not found.");

        if (correctedDonationDate is { } newDate)
        {
            if (newDate > clock.TodayUtc || newDate < clock.TodayUtc.AddDays(-_options.MaxReportAgeDays * 3))
            {
                throw AppException.Invalid("Corrected donation date is out of the allowed range.");
            }

            await conn.ExecuteAsync(new CommandDefinition(
                "update donation_records set donation_date = @newDate where id = @recordId",
                new { newDate, recordId }, tx, cancellationToken: ct));

            var policy = record.Policy_version_id is not null
                ? await policies.GetByIdAsync(conn, tx, record.Policy_version_id.Value, ct)
                : await policies.GetActiveAsync(conn, tx, ct: ct);

            DateTimeOffset? eligibleAgain = policy is null
                ? null
                : DeferralCalculator.EligibleAgain(policy, record.Status == DonationStateMachine.Rejected
                    ? DonationStateMachine.Rejected
                    : DonationStateMachine.Corrected, newDate);

            await conn.ExecuteAsync(new CommandDefinition(
                "update donation_records set eligible_again_at = @eligibleAgain where id = @recordId",
                new { eligibleAgain, recordId }, tx, cancellationToken: ct));

            await RevokeDeferralsForRecordAsync(conn, tx, record.Id, "superseded by administrative correction", ct);

            if (policy is not null && eligibleAgain is not null
                && DonationStateMachine.CountsAsDonation(record.Status))
            {
                await InsertDeferralAsync(conn, tx, record.Donor_user_id, record.Id, DeferralTypes.Verified,
                    policy.Id, eligibleAgain.Value, ct);
            }

            await SetStatusAsync(conn, tx, record.Id, DonationStateMachine.Corrected, adminUserId, ct);
            await InsertEventAsync(conn, tx, record.Id, "corrected", adminUserId, record.Status,
                DonationStateMachine.Corrected, new { newDate, note, eligibleAgain }, ct);

            await outbox.EnqueueAsync(conn, tx, record.Donor_user_id, record.Request_id,
                NotificationTypes.DonationCorrected,
                "Donation record corrected",
                $"An administrator corrected your donation record. You are deferred until {eligibleAgain:yyyy-MM-dd} (UTC). " +
                "Final eligibility is determined by the donation centre.",
                record.Request_id is null ? null : $"affoundation://requests/{record.Request_id}", ct);
        }
        else
        {
            await InsertEventAsync(conn, tx, record.Id, "note_added", adminUserId, record.Status, record.Status,
                new { note }, ct);
        }

        await audit.WriteAsync(conn, tx, adminUserId, "donation.corrected", "donation_record",
            record.Id.ToString(), new { correctedDonationDate, note }, correlationId, ct);

        await tx.CommitAsync(ct);
        return (await GetAsync(recordId, ct))!;
    }

    // ------------------------------------------------------------------ reads

    public async Task<DonationRecordDto?> GetAsync(Guid recordId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("""
            select dr.id, dr.request_id, dr.donor_user_id, dr.reported_by_user_id, dr.donation_date,
                   dr.status, p.version as policy_version, dr.eligible_again_at,
                   dr.confirmed_by_user_id, dr.confirmed_at_utc, dr.notes, dr.created_at_utc
              from donation_records dr
              left join deferral_policy_versions p on p.id = dr.policy_version_id
             where dr.id = @recordId
            """,
            new { recordId }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<DonationRecordDto?> GetByRequestAsync(Guid requestId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("""
            select dr.id, dr.request_id, dr.donor_user_id, dr.reported_by_user_id, dr.donation_date,
                   dr.status, p.version as policy_version, dr.eligible_again_at,
                   dr.confirmed_by_user_id, dr.confirmed_at_utc, dr.notes, dr.created_at_utc
              from donation_records dr
              left join deferral_policy_versions p on p.id = dr.policy_version_id
             where dr.request_id = @requestId and dr.status <> 'rejected'
            """,
            new { requestId }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<PagedResult<DonationRecordDto>> ListMineAsync(Guid donorUserId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecordRow>(new CommandDefinition("""
            select dr.id, dr.request_id, dr.donor_user_id, dr.reported_by_user_id, dr.donation_date,
                   dr.status, p.version as policy_version, dr.eligible_again_at,
                   dr.confirmed_by_user_id, dr.confirmed_at_utc, dr.notes, dr.created_at_utc
              from donation_records dr
              left join deferral_policy_versions p on p.id = dr.policy_version_id
             where dr.donor_user_id = @donorUserId
             order by dr.donation_date desc
             limit @limit offset @offset
            """,
            new { donorUserId, limit = pageSize, offset = (page - 1) * pageSize }, cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from donation_records where donor_user_id = @donorUserId",
            new { donorUserId }, cancellationToken: ct));

        return new PagedResult<DonationRecordDto>(rows.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<PagedResult<DonationRecordDto>> ListForReviewAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecordRow>(new CommandDefinition("""
            select dr.id, dr.request_id, dr.donor_user_id, dr.reported_by_user_id, dr.donation_date,
                   dr.status, p.version as policy_version, dr.eligible_again_at,
                   dr.confirmed_by_user_id, dr.confirmed_at_utc, dr.notes, dr.created_at_utc
              from donation_records dr
              left join deferral_policy_versions p on p.id = dr.policy_version_id
             where (@status::text is null and dr.status in ('reported','disputed')) or dr.status = @status
             order by dr.created_at_utc asc
             limit @limit offset @offset
            """,
            new { status, limit = pageSize, offset = (page - 1) * pageSize }, cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*) from donation_records
             where (@status::text is null and status in ('reported','disputed')) or status = @status
            """,
            new { status }, cancellationToken: ct));

        return new PagedResult<DonationRecordDto>(rows.Select(Map).ToList(), total, page, pageSize);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<RecordRow> LoadRecordForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid recordId, CancellationToken ct)
    {
        return await conn.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("""
            select id, request_id, donor_user_id, reported_by_user_id, donation_date, status,
                   policy_version_id, eligible_again_at, confirmed_by_user_id, confirmed_at_utc,
                   notes, created_at_utc
              from donation_records
             where id = @recordId
             for update
            """,
            new { recordId }, tx, cancellationToken: ct))
            ?? throw AppException.NotFound("Donation record not found.");
    }

    private static async Task<RecordRow?> LoadActiveRecordByRequestAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid requestId, CancellationToken ct)
    {
        var id = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select id from donation_records where request_id = @requestId and status <> 'rejected'
            """,
            new { requestId }, tx, cancellationToken: ct));
        return id is null ? null : await LoadRecordForUpdateAsync(conn, tx, id.Value, ct);
    }

    private static void RequireTransition(RecordRow record, string to, params string[] allowedFrom)
    {
        if (record is null)
        {
            throw AppException.NotFound("Donation record not found.");
        }

        if (!allowedFrom.Contains(record.Status) || !DonationStateMachine.CanTransition(record.Status, to))
        {
            throw AppException.Conflict($"Cannot move a {record.Status} donation record to {to}.");
        }
    }

    private static Task SetStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid recordId, string status,
        Guid? confirmedBy, CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            update donation_records
               set status = @status,
                   confirmed_by_user_id = coalesce(@confirmedBy, confirmed_by_user_id),
                   confirmed_at_utc = case when @confirmedBy is null then confirmed_at_utc else now() end,
                   updated_at_utc = now()
             where id = @recordId
            """,
            new { status, confirmedBy, recordId }, tx, cancellationToken: ct));
    }

    private static Task InsertEventAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid recordId, string eventType,
        Guid? actorId, string? fromStatus, string? toStatus, object details, CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            insert into donation_record_events
                (donation_record_id, event_type, actor_user_id, from_status, to_status, details)
            values (@recordId, @eventType, @actorId, @fromStatus, @toStatus, @details::jsonb)
            """,
            new
            {
                recordId,
                eventType,
                actorId,
                fromStatus,
                toStatus,
                details = System.Text.Json.JsonSerializer.Serialize(details),
            }, tx, cancellationToken: ct));
    }

    private static Task InsertDeferralAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid donorUserId, Guid recordId,
        string deferralType, Guid policyId, DateTimeOffset eligibleAgainAt, CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            insert into donor_deferrals
                (donor_user_id, donation_record_id, deferral_type, policy_version_id, eligible_again_at)
            values (@donorUserId, @recordId, @deferralType, @policyId, @eligibleAgainAt)
            """,
            new { donorUserId, recordId, deferralType, policyId, eligibleAgainAt },
            tx, cancellationToken: ct));
    }

    private static Task RevokeDeferralsForRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid recordId, string reason, CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            update donor_deferrals
               set revoked_at_utc = now(), revoked_reason = @reason
             where donation_record_id = @recordId and revoked_at_utc is null
            """,
            new { recordId, reason }, tx, cancellationToken: ct));
    }

    private async Task NotifyAdministratorsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid? requestId,
        string type, string title, string body, CancellationToken ct)
    {
        var adminIds = await conn.QueryAsync<Guid>(new CommandDefinition("""
            select ur."UserId"
              from "AspNetUserRoles" ur
              join "AspNetRoles" r on r."Id" = ur."RoleId"
             where r."NormalizedName" = 'ADMINISTRATOR'
            """,
            parameters: null,
            transaction: tx,
            cancellationToken: ct));

        foreach (var adminId in adminIds)
        {
            await outbox.EnqueueAsync(conn, tx, adminId, requestId, type, title, body,
                requestId is null ? null : $"affoundation://requests/{requestId}", ct);
        }
    }

    private static DonationRecordDto Map(RecordRow r) => new(
        r.Id, r.Request_id, r.Donor_user_id, r.Reported_by_user_id, r.Donation_date, r.Status,
        r.Policy_version, r.Eligible_again_at, r.Confirmed_by_user_id, r.Confirmed_at_utc,
        r.Notes, r.Created_at_utc);

    private sealed class RequestLockRow
    {
        public Guid Id { get; init; }
        public Guid Requester_id { get; init; }
        public string Status { get; init; } = null!;
        public Guid? Matched_donor_user_id { get; init; }
    }

    private sealed class RecordRow
    {
        public Guid Id { get; init; }
        public Guid? Request_id { get; init; }
        public Guid Donor_user_id { get; init; }
        public Guid Reported_by_user_id { get; init; }
        public DateOnly Donation_date { get; init; }
        public string Status { get; init; } = null!;
        public Guid? Policy_version_id { get; init; }
        public string? Policy_version { get; init; }
        public DateTimeOffset? Eligible_again_at { get; init; }
        public Guid? Confirmed_by_user_id { get; init; }
        public DateTimeOffset? Confirmed_at_utc { get; init; }
        public string? Notes { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
    }
}
