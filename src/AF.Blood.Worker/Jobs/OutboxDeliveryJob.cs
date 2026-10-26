using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Application.Features.Notifications;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AF.Blood.Worker.Jobs;

/// <summary>Delivers queued notifications through the push dispatcher with
/// bounded retries, jittered backoff, and lease recovery (ADR-0006).</summary>
public sealed class OutboxDeliveryJob(
    NpgsqlDataSource db,
    IPushDispatcher push,
    IOptions<Application.Options.NotificationOptions> notificationOptions,
    IOptions<Application.Options.WorkerOptions> workerOptions,
    ILogger<OutboxDeliveryJob> logger) : BackgroundService
{
    private readonly Random _random = Random.Shared;
    private Application.Options.NotificationOptions Options => notificationOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(2, workerOptions.Value.PollSeconds / 2.0));
        using var timer = new PeriodicTimer(pollInterval);

        logger.LogInformation("OutboxDeliveryJob started (poll every {Seconds}s).", pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var recovered = await RecoverExpiredLeasesAsync(stoppingToken);
                if (recovered > 0)
                {
                    logger.LogWarning("Recovered {Count} abandoned outbox lease(s).", recovered);
                }

                var delivered = await DeliverBatchAsync(stoppingToken);
                if (delivered > 0)
                {
                    logger.LogInformation("Delivered {Count} notification(s).", delivered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxDeliveryJob cycle failed; retrying next tick.");
            }
        }

        logger.LogInformation("OutboxDeliveryJob stopping.");
    }

    private async Task<int> RecoverExpiredLeasesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("""
            update notification_outbox
               set status = 'pending', leased_by = null, leased_until = null
             where status = 'processing' and leased_until is not null and leased_until < now()
            """,
            cancellationToken: ct));
    }

    private async Task<int> DeliverBatchAsync(CancellationToken ct)
    {
        var batch = Options.OutboxBatchSize;
        var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(Options.LeaseMinutes);

        List<OutboxRow> claimed;
        await using (var conn = await db.OpenConnectionAsync(ct))
        {
            claimed = (await conn.QueryAsync<OutboxRow>(new CommandDefinition("""
                update notification_outbox
                   set status = 'processing', leased_by = 'outbox-worker', leased_until = @leaseUntil,
                       last_attempt_at_utc = now()
                 where id in (
                       select id from notification_outbox
                        where status = 'pending' and available_at_utc <= now()
                        order by created_at_utc asc
                        for update skip locked
                        limit @batch)
                returning id, notification_id as notificationid, user_id as userid
                """,
                new { leaseUntil, batch }, cancellationToken: ct))).ToList();
        }

        var deliveredCount = 0;
        foreach (var row in claimed)
        {
            ct.ThrowIfCancellationRequested();
            deliveredCount += await DeliverOneAsync(row, ct) ? 1 : 0;
        }

        return deliveredCount;
    }

    private async Task<bool> DeliverOneAsync(OutboxRow row, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var notification = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Type, string Title, string Body, string? DeepLink, Guid UserId)>(
            new CommandDefinition("""
                select id, type, title, body, deep_link, user_id
                  from notification_history where id = @notificationId
                """,
                new { notificationId = row.NotificationId }, tx, cancellationToken: ct));

        if (notification == default)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "update notification_outbox set status = 'skipped', last_error = 'notification missing' where id = @id",
                new { id = row.Id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return false;
        }

        var subscriptions = (await conn.QueryAsync<string>(new CommandDefinition("""
            select onesignal_subscription_id from user_devices
             where user_id = @userId and is_active
            """,
            new { userId = notification.UserId }, tx, cancellationToken: ct))).ToList();

        if (subscriptions.Count == 0)
        {
            // Nothing to send: the notification remains visible in the in-app inbox.
            await conn.ExecuteAsync(new CommandDefinition("""
                update notification_outbox set status = 'skipped', last_error = 'no active devices' where id = @id
                """,
                new { id = row.Id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return false;
        }

        PushDeliveryResult result;
        try
        {
            result = await push.DispatchAsync(new PushMessage(
                subscriptions, notification.Title, notification.Body, notification.DeepLink, notification.Id), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new PushDeliveryResult(false, null, ex.Message[..Math.Min(ex.Message.Length, 500)]);
        }

        if (result.Success)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update notification_outbox set status = 'delivered', delivered_at_utc = now(), last_error = null
                 where id = @id
                """,
                new { id = row.Id }, tx, cancellationToken: ct));

            if (result.ProviderMessageId is not null)
            {
                // Delivered-at is only recorded when the provider actually
                // accepted the message; a no-op dispatcher never fakes it.
                await conn.ExecuteAsync(new CommandDefinition("""
                    update notification_history
                       set delivered_at_utc = now(), provider_message_id = @providerMessageId
                     where id = @notificationId and delivered_at_utc is null
                    """,
                    new { providerMessageId = result.ProviderMessageId, notificationId = notification.Id },
                    tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
            return true;
        }

        var attempts = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select attempts from notification_outbox where id = @id", new { id = row.Id },
            tx, cancellationToken: ct));
        attempts += 1;
        var failed = attempts >= Options.MaxAttempts;

        await conn.ExecuteAsync(new CommandDefinition("""
            update notification_outbox
               set status = case when @failed then 'failed' else 'pending' end,
                   attempts = @attempts,
                   available_at_utc = case when @failed then available_at_utc
                                           else now() + make_interval(secs => @retrySeconds) end,
                   last_error = @error
             where id = @id
            """,
            new
            {
                failed,
                attempts,
                retrySeconds = (int)Backoff.Delay(attempts,
                    TimeSpan.FromSeconds(Options.RetryBaseSeconds),
                    TimeSpan.FromSeconds(Options.RetryMaxSeconds), _random).TotalSeconds,
                error = result.Error?[..Math.Min(result.Error.Length, 500)],
                id = row.Id,
            }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return false;
    }

    private sealed class OutboxRow
    {
        public Guid Id { get; init; }
        public Guid NotificationId { get; init; }
        public Guid UserId { get; init; }
    }
}
