using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Application.Features.Audit;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AF.Blood.Application.Features.Requests;

/// <summary>
/// Server-side request expiry plus token housekeeping. Eligibility never
/// *depends* on this running on time: every eligibility check reads persisted
/// state, so a delayed run cannot grant eligibility early (spec §17).
/// </summary>
public sealed class ExpiryService(NpgsqlDataSource db, AuditWriter audit, ILogger<ExpiryService> logger)
{
    /// <summary>Expires open requests past their expiry instant and cancels their
    /// pending matching runs. Returns the number of requests expired.</summary>
    public async Task<int> ExpireDueRequestsAsync(string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var expired = (await conn.QueryAsync<Guid>(new CommandDefinition("""
            update donation_requests
               set status = @expired, updated_at_utc = now()
             where status = @open and expires_at_utc <= now()
            returning id
            """,
            new { expired = RequestStateMachine.Expired, open = RequestStateMachine.Open },
            tx, cancellationToken: ct))).ToList();

        foreach (var requestId in expired)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update matching_runs
                   set status = 'cancelled', completed_at_utc = now()
                 where request_id = @requestId and status in ('pending','processing')
                """,
                new { requestId }, tx, cancellationToken: ct));

            await audit.WriteAsync(conn, tx, null, "request.expired", "donation_request",
                requestId.ToString(), new { }, correlationId, ct);
        }

        await tx.CommitAsync(ct);

        if (expired.Count > 0)
        {
            logger.LogInformation("Expired {Count} donation requests.", expired.Count);
        }

        return expired.Count;
    }

    /// <summary>Deletes stale verification codes and long-expired refresh tokens.</summary>
    public async Task HousekeepingAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);

        var codes = await conn.ExecuteAsync(new CommandDefinition("""
            delete from verification_codes
             where expires_at_utc < now() - interval '1 day'
                or (consumed_at_utc is not null and consumed_at_utc < now() - interval '1 day')
            """,
            cancellationToken: ct));

        var tokens = await conn.ExecuteAsync(new CommandDefinition("""
            delete from auth_refresh_tokens
             where expires_at_utc < now() - interval '30 days'
                or (revoked_at_utc is not null and revoked_at_utc < now() - interval '30 days')
            """,
            cancellationToken: ct));

        if (codes > 0 || tokens > 0)
        {
            logger.LogDebug("Housekeeping removed {Codes} verification codes and {Tokens} refresh tokens.", codes, tokens);
        }
    }
}
