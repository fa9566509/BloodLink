using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Notifications;

/// <summary>
/// Transactional-outbox enqueue helper. Every notification is first written to
/// <c>notification_history</c> (the user-visible inbox) and then to
/// <c>notification_outbox</c>, both inside the caller's transaction. The worker
/// performs the actual OneSignal delivery later (ADR-0006).
/// </summary>
public sealed class OutboxEnqueuer
{
    /// <summary>Enqueues a notification on an existing connection/transaction.
    /// Returns the notification (history) id.</summary>
    public async Task<Guid> EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid userId,
        Guid? requestId,
        string type,
        string title,
        string body,
        string? deepLink,
        CancellationToken ct = default)
    {
        var notificationId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into notification_history (user_id, request_id, type, title, body, deep_link)
            values (@userId, @requestId, @type, @title, @body, @deepLink)
            returning id
            """,
            new { userId, requestId, type, title, body, deepLink },
            tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition("""
            insert into notification_outbox (notification_id, user_id)
            values (@notificationId, @userId)
            """,
            new { notificationId, userId },
            tx,
            cancellationToken: ct));

        return notificationId;
    }
}
