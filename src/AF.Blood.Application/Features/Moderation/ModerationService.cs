using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Contracts;
using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Moderation;

public sealed class ModerationService(NpgsqlDataSource db, AuditWriter audit, OutboxEnqueuer outbox)
{
    public async Task<AbuseReportDto> CreateAbuseReportAsync(
        Guid reporterId, CreateAbuseReportRequest request, string? correlationId, CancellationToken ct)
    {
        if (request.TargetUserId is null && request.TargetRequestId is null)
        {
            throw AppException.Invalid("A report must target a user or a request.");
        }

        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Length > 64)
        {
            throw AppException.Invalid("Category is required (max 64 characters).");
        }

        if (string.IsNullOrWhiteSpace(request.Details) || request.Details.Length > 2000)
        {
            throw AppException.Invalid("Details are required (max 2000 characters).");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into abuse_reports (reported_by_user_id, target_user_id, target_request_id, category, details)
            values (@reporterId, @targetUserId, @targetRequestId, @category, @details)
            returning id
            """,
            new
            {
                reporterId,
                request.TargetUserId,
                request.TargetRequestId,
                category = request.Category.Trim(),
                details = request.Details.Trim(),
            },
            tx, cancellationToken: ct));

        await audit.WriteAsync(conn, tx, reporterId, "abuse_report.created", "abuse_report",
            id.ToString(), new { request.TargetUserId, request.TargetRequestId, request.Category }, correlationId, ct);

        await tx.CommitAsync(ct);

        return new AbuseReportDto(id, reporterId, request.TargetUserId, request.TargetRequestId,
            request.Category.Trim(), request.Details.Trim(), "open", null, clockPlaceholder(), null);
    }

    public async Task<PagedResult<AbuseReportDto>> ListAbuseReportsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ReportRow>(new CommandDefinition("""
            select id, reported_by_user_id, target_user_id, target_request_id, category, details,
                   status, resolution_note, created_at_utc, resolved_at_utc
              from abuse_reports
             where (@status::text is null or status = @status)
             order by created_at_utc desc
             limit @limit offset @offset
            """,
            new { status, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from abuse_reports where (@status::text is null or status = @status)",
            new { status }, cancellationToken: ct));

        var items = rows.Select(r => new AbuseReportDto(r.Id, r.Reported_by_user_id, r.Target_user_id,
            r.Target_request_id, r.Category, r.Details, r.Status, r.Resolution_note,
            r.Created_at_utc, r.Resolved_at_utc)).ToList();

        return new PagedResult<AbuseReportDto>(items, total, page, pageSize);
    }

    public async Task<AbuseReportDto> ResolveAbuseReportAsync(
        Guid adminId, Guid reportId, ResolveAbuseReportRequest request, string? correlationId, CancellationToken ct)
    {
        var outcome = request.Outcome switch
        {
            "resolved" => "resolved",
            "dismissed" => "dismissed",
            _ => throw AppException.Invalid("Outcome must be 'resolved' or 'dismissed'."),
        };

        await using var conn = await db.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition("""
            update abuse_reports
               set status = case when @outcome = 'resolved' then 'resolved' else 'dismissed' end,
                   resolution_note = @note,
                   resolved_by_user_id = @adminId,
                   resolved_at_utc = now()
             where id = @reportId and status in ('open','reviewing')
            """,
            new { outcome, note = request.Note, adminId, reportId }, cancellationToken: ct));

        if (updated == 0)
        {
            throw AppException.NotFound("Open abuse report not found.");
        }

        await audit.WriteAsync(conn, null, adminId, "abuse_report.resolved", "abuse_report",
            reportId.ToString(), new { request.Outcome, request.Note }, correlationId, ct);

        var row = await conn.QuerySingleAsync<ReportRow>(new CommandDefinition(
            "select id, reported_by_user_id, target_user_id, target_request_id, category, details, status, resolution_note, created_at_utc, resolved_at_utc from abuse_reports where id = @reportId",
            new { reportId }, cancellationToken: ct));

        return new AbuseReportDto(row.Id, row.Reported_by_user_id, row.Target_user_id, row.Target_request_id,
            row.Category, row.Details, row.Status, row.Resolution_note, row.Created_at_utc, row.Resolved_at_utc);
    }

    public async Task SuspendUserAsync(
        Guid adminId, Guid targetUserId, string reason, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw AppException.Invalid("A suspension reason is required.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        if (await IsAdministratorAsync(conn, tx, targetUserId, ct))
        {
            throw AppException.Forbidden("Administrator accounts cannot be suspended through the API.");
        }

        var alreadySuspended = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (select 1 from account_suspensions
                            where user_id = @targetUserId and lifted_at_utc is null)
            """,
            new { targetUserId }, tx, cancellationToken: ct));

        if (alreadySuspended)
        {
            throw AppException.Conflict("This account is already suspended.");
        }

        await conn.ExecuteAsync(new CommandDefinition("""
            insert into account_suspensions (user_id, suspended_by_user_id, reason)
            values (@targetUserId, @adminId, @reason)
            """,
            new { targetUserId, adminId, reason = reason.Trim() }, tx, cancellationToken: ct));

        // Suspension revokes every refresh session immediately; access tokens
        // lapse within their short remaining lifetime and every sensitive
        // server-side action re-checks suspension.
        await conn.ExecuteAsync(new CommandDefinition("""
            update auth_refresh_tokens
               set revoked_at_utc = now(), revoked_reason = 'account suspended'
             where user_id = @targetUserId and revoked_at_utc is null
            """,
            new { targetUserId }, tx, cancellationToken: ct));

        await outbox.EnqueueAsync(conn, tx, targetUserId, null,
            NotificationTypes.AccountSuspended,
            "Account suspended",
            "Your account has been suspended by a moderator. Contact support if you believe this is a mistake.",
            null, ct);

        await audit.WriteAsync(conn, tx, adminId, "account.suspended", "user",
            targetUserId.ToString(), new { reason }, correlationId, ct);

        await tx.CommitAsync(ct);
    }

    public async Task RestoreUserAsync(
        Guid adminId, Guid targetUserId, string? reason, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var updated = await conn.ExecuteAsync(new CommandDefinition("""
            update account_suspensions
               set lifted_at_utc = now(), lifted_by_user_id = @adminId, lift_reason = @reason
             where user_id = @targetUserId and lifted_at_utc is null
            """,
            new { adminId, reason, targetUserId }, tx, cancellationToken: ct));

        if (updated == 0)
        {
            throw AppException.NotFound("No active suspension found for this account.");
        }

        await outbox.EnqueueAsync(conn, tx, targetUserId, null,
            NotificationTypes.AccountRestored,
            "Account restored",
            "Your account suspension has been lifted. Welcome back.",
            null, ct);

        await audit.WriteAsync(conn, tx, adminId, "account.restored", "user",
            targetUserId.ToString(), new { reason }, correlationId, ct);

        await tx.CommitAsync(ct);
    }

    /// <summary>True when the account has an active suspension. Called on login
    /// and before sensitive server-side actions.</summary>
    public async Task<bool> IsSuspendedAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (select 1 from account_suspensions where user_id = @userId and lifted_at_utc is null)
            """,
            new { userId }, cancellationToken: ct));
    }

    private static async Task<bool> IsAdministratorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid userId, CancellationToken ct)
    {
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (
                select 1
                  from "AspNetUserRoles" ur
                  join "AspNetRoles" r on r."Id" = ur."RoleId"
                 where ur."UserId" = @userId and r."NormalizedName" = 'ADMINISTRATOR')
            """,
            new { userId }, tx, cancellationToken: ct));
    }

    private static DateTimeOffset clockPlaceholder() => DateTimeOffset.UtcNow;

    private sealed class ReportRow
    {
        public Guid Id { get; init; }
        public Guid Reported_by_user_id { get; init; }
        public Guid? Target_user_id { get; init; }
        public Guid? Target_request_id { get; init; }
        public string Category { get; init; } = null!;
        public string Details { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string? Resolution_note { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
        public DateTimeOffset? Resolved_at_utc { get; init; }
    }
}
