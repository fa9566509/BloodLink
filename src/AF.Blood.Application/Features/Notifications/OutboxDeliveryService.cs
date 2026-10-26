using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Domain;
using AF.Blood.Application.Options;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AF.Blood.Application.Features.Notifications;

/// <summary>
/// Outbox delivery: claims queued notifications with SKIP LOCKED leasing, sends
/// them through the push dispatcher, records outcomes, retries transient
/// failures with bounded jittered backoff, and recovers leases abandoned by a
/// crashed worker (ADR-0006). Delivery is at-least-once; providers dedupe.
/// </summary>
public sealed class OutboxDeliveryService(
    NpgsqlDataSource db,
    IPushDispatcher push,
    IOptions<NotificationOptions> options,
    ILogger<OutboxDeliveryService> logger)
{
    private readonly Random _random = Random.Shared;
    private NotificationOptions Options => options.Value;

    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("""
            update notification_outbox
               set status = 'pending', leased_by = null, leased_until = null
             where status = 'processing' and leased_until is not null and leased_until < now()
            """,
            cancellationToken: ct));
    }

    /// <summary>Delivers one batch of due notifications. Returns the number
    /// actually dispatched to the provider (skips excluded).</summary>
    public async Task<int> DeliverBatchAsync(CancellationToken ct)
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

        var delivered = 0;
        foreach (var row in claimed)
        {
            ct.ThrowIfCancellationRequested();
            if (await DeliverOneAsync(row, ct))
            {
                delivered++;
            }
        }

        return delivered;
    }

    private async Task<bool> DeliverOneAsync(OutboxRow row, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var notification = await conn.QuerySingleOrDefaultAsync<NotificationPayloadRow>(new CommandDefinition("""
            select id, type, title, body, deep_link, user_id
              from notification_history where id = @notificationId
            """,
            new { notificationId = row.NotificationId }, tx, cancellationToken: ct));

        if (notification is null)
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
            new { userId = notification.User_id }, tx, cancellationToken: ct))).ToList();

        if (subscriptions.Count == 0)
        {
            // Nothing to send: the notification remains visible in the in-app inbox.
            await conn.ExecuteAsync(new CommandDefinition("""
                update notification_outbox set status = 'skipped', last_error = 'no active devices' where id = @id
                """,
                new { id = row.Id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            logger.LogDebug("Notification {NotificationId} skipped: no active devices.", notification.Id);
            return false;
        }

        PushDeliveryResult result;
        try
        {
            result = await push.DispatchAsync(new PushMessage(
                subscriptions, notification.Title, notification.Body, notification.Deep_link, notification.Id), ct);
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
                // Delivered-at is recorded only when a real provider accepted the
                // message; a no-op/test dispatcher never fakes it.
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

    private sealed class NotificationPayloadRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Body { get; init; } = null!;
        public string? Deep_link { get; init; }
        public Guid User_id { get; init; }
    }
}
