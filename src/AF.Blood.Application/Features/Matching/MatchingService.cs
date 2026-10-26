using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Application.Features.Shared;
using AF.Blood.Application.Options;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AF.Blood.Application.Features.Matching;

/// <summary>
/// Wave-based donor matching. Candidate selection is a parameterised PostGIS
/// query; the compatibility group list comes from
/// <see cref="BloodCompatibility"/> (tested separately from the query).
/// Runs are leased with FOR UPDATE SKIP LOCKED so several workers can operate
/// concurrently, and every wave revalidates request eligibility (ADR-0006).
/// </summary>
public sealed class MatchingService(
    NpgsqlDataSource db,
    IClock clock,
    OutboxEnqueuer outbox,
    IOptions<MatchingOptions> options,
    ILogger<MatchingService> logger)
{
    private readonly MatchingOptions _options = options.Value;

    public static async Task CreateRunAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid requestId, int waveIndex, int radiusKm,
        DateTimeOffset now, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into matching_runs (request_id, wave_index, radius_km, due_at_utc)
            values (@requestId, @waveIndex, @radiusKm, @now)
            """,
            new { requestId, waveIndex, radiusKm, now },
            tx,
            cancellationToken: ct));
    }

    public static async Task CancelPendingRunsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid requestId, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
            update matching_runs set status = 'cancelled', completed_at_utc = now()
             where request_id = @requestId and status in ('pending','processing')
            """,
            new { requestId },
            tx,
            cancellationToken: ct));
    }

    /// <summary>Claims and processes up to <paramref name="maxRuns"/> due runs.
    /// Returns the number processed. Called by the worker on a timer.</summary>
    public async Task<int> ProcessDueRunsAsync(string instanceName, int maxRuns, CancellationToken ct)
    {
        var processed = 0;
        for (var i = 0; i < maxRuns; i++)
        {
            ct.ThrowIfCancellationRequested();
            var claimed = await ClaimNextRunAsync(instanceName, ct);
            if (claimed is null)
            {
                break;
            }

            try
            {
                await ProcessRunAsync(claimed, instanceName, ct);
                processed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Matching run {RunId} failed; it will be retried after lease recovery.",
                    claimed.RunId);
                await MarkRunFailedAsync(claimed.RunId, ex.Message, ct);
            }
        }

        return processed;
    }

    private async Task<ClaimedRun?> ClaimNextRunAsync(string instanceName, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var leaseUntil = clock.UtcNow.AddMinutes(_options.LeaseMinutes);
        return await conn.QuerySingleOrDefaultAsync<ClaimedRun>(new CommandDefinition("""
            update matching_runs
               set status = 'processing', leased_by = @instanceName, leased_until = @leaseUntil,
                   started_at_utc = coalesce(started_at_utc, now())
             where id = (
                   select id from matching_runs
                    where status = 'pending' and due_at_utc <= now()
                    order by due_at_utc asc
                    for update skip locked
                    limit 1)
            returning id as runid, request_id as requestid, wave_index, radius_km
            """,
            new { instanceName, leaseUntil },
            cancellationToken: ct));
    }

    private async Task ProcessRunAsync(ClaimedRun run, string instanceName, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Revalidate the request. Closed or expired requests stop waves.
        var request = await conn.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition("""
            select id, requester_id, blood_group, urgency, status, expires_at_utc,
                   st_y(destination::geometry) as lat, st_x(destination::geometry) as lon
              from donation_requests
             where id = @requestId
            """,
            new { requestId = run.RequestId },
            tx,
            cancellationToken: ct));

        if (request is null)
        {
            await CompleteRunAsync(conn, tx, run.RunId, 0, 0, "request missing", ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (RequestStateMachine.IsClosed(request.Status)
            || request.Status == RequestStateMachine.UnderReview
            || request.Expires_at_utc <= clock.UtcNow)
        {
            await CompleteRunAsync(conn, tx, run.RunId, 0, 0, $"request {request.Status}", ct);
            await tx.CommitAsync(ct);
            return;
        }

        // 2. Select candidates for this wave.
        var compatibleDonorGroups = BloodCompatibility.CompatibleDonorGroups(request.Blood_group);
        var (compatibleAny, compatibleParams) = SqlParamExpansion.AnyOf("g", compatibleDonorGroups);
        var candidateParams = new DynamicParameters();
        candidateParams.AddDynamicParams(compatibleParams);
        candidateParams.AddDynamicParams(new
        {
            requestId = run.RequestId,
            requesterId = request.Requester_id,
            lat = request.Lat,
            lon = request.Lon,
            radiusMetres = (double)(run.Radius_km * 1000),
            batchSize = _options.BatchSizePerWave,
        });
        var candidates = (await conn.QueryAsync<CandidateRow>(new CommandDefinition($"""
            select dp.user_id,
                   st_distance(dp.location, st_setsrid(st_makepoint(@lon, @lat), 4326)::geography) as distance_m
              from donor_profiles dp
             where dp.is_discoverable
               and dp.is_available
               and dp.location is not null
               and dp.blood_group = {compatibleAny}
               and st_dwithin(dp.location, st_setsrid(st_makepoint(@lon, @lat), 4326)::geography, @radiusMetres)
               and dp.user_id <> @requesterId
               and not exists (select 1 from donor_deferrals d
                               where d.donor_user_id = dp.user_id
                                 and d.revoked_at_utc is null
                                 and d.eligible_again_at > now())
               and not exists (select 1 from account_suspensions s
                               where s.user_id = dp.user_id and s.lifted_at_utc is null)
               and not exists (select 1 from matching_run_donors m
                               where m.request_id = @requestId and m.donor_user_id = dp.user_id)
               and not exists (select 1 from request_responses rr
                               where rr.request_id = @requestId and rr.donor_user_id = dp.user_id)
             order by distance_m asc, dp.user_id asc
             limit @batchSize
            """,
            candidateParams,
            tx,
            cancellationToken: ct))).ToList();

        // 3. Persist membership (dedup guard) and queue notifications atomically.
        var notified = 0;
        foreach (var candidate in candidates)
        {
            var inserted = await conn.ExecuteAsync(new CommandDefinition("""
                insert into matching_run_donors (matching_run_id, request_id, donor_user_id, distance_m)
                values (@runId, @requestId, @donorUserId, @distanceM)
                on conflict (matching_run_id, donor_user_id) do nothing
                """,
                new { runId = run.RunId, requestId = run.RequestId, donorUserId = candidate.User_id, distanceM = candidate.Distance_m },
                tx,
                cancellationToken: ct));

            if (inserted == 0)
            {
                continue;
            }

            await outbox.EnqueueAsync(conn, tx, candidate.User_id, run.RequestId,
                NotificationTypes.RequestMatchedDonorAlert,
                "Blood needed near you",
                $"A {request.Urgency} whole-blood request ({request.Blood_group}) is open about " +
                $"{Math.Max(1, (int)Math.Round(candidate.Distance_m / 1000.0))} km away. Open the app to respond.",
                $"affoundation://requests/{run.RequestId}",
                ct);
            notified++;
        }

        // 4. Complete this run and schedule the next wave if the rules say so.
        await CompleteRunAsync(conn, tx, run.RunId, candidates.Count, notified, null, ct);

        var decision = WavePlanner.DecideNext(
            run.Wave_index, run.Radius_km, notified,
            _options.RadiiKm, _options.MinCandidatesToStop,
            TimeSpan.FromMinutes(_options.WaveDelayMinutes));

        if (decision.ScheduleNext)
        {
            var dueAt = clock.UtcNow.Add(decision.Delay);
            await CreateRunAsync(conn, tx, run.RequestId, decision.NextWaveIndex, decision.NextRadiusKm, dueAt, ct);
        }

        await tx.CommitAsync(ct);
    }

    private static Task CompleteRunAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid runId, int candidates, int notified,
        string? note, CancellationToken ct)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            update matching_runs
               set status = 'completed', candidates_found = @candidates, notified_count = @notified,
                   failure_reason = @note, completed_at_utc = now(), leased_by = null, leased_until = null
             where id = @runId
            """,
            new { runId, candidates, notified, note },
            tx,
            cancellationToken: ct));
    }

    private async Task MarkRunFailedAsync(Guid runId, string reason, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition("""
                update matching_runs
                   set status = case when attempts_exhausted then 'failed' else 'pending' end,
                       failure_reason = @reason,
                       leased_by = null, leased_until = null
                  from (select (count(*) > 5) as attempts_exhausted
                          from matching_runs where id = @runId) stat
                 where id = @runId
                """,
                new { runId, reason = reason[..Math.Min(reason.Length, 500)] },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark matching run {RunId} failed.", runId);
        }
    }

    /// <summary>Resets runs whose worker died mid-processing (lease expired).</summary>
    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("""
            update matching_runs
               set status = 'pending', leased_by = null, leased_until = null
             where status = 'processing' and leased_until is not null and leased_until < now()
            """,
            cancellationToken: ct));
    }

    private sealed class ClaimedRun
    {
        public Guid RunId { get; init; }
        public Guid RequestId { get; init; }
        public int Wave_index { get; init; }
        public int Radius_km { get; init; }
    }

    private sealed class RequestRow
    {
        public Guid Id { get; init; }
        public Guid Requester_id { get; init; }
        public string Blood_group { get; init; } = null!;
        public string Urgency { get; init; } = null!;
        public string Status { get; init; } = null!;
        public DateTimeOffset Expires_at_utc { get; init; }
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    private sealed class CandidateRow
    {
        public Guid User_id { get; init; }
        public double Distance_m { get; init; }
    }
}
