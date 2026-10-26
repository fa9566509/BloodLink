using AF.Blood.Application.Abstractions;
using AF.Blood.Contracts;
using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Notifications;

public sealed class NotificationService(NpgsqlDataSource db)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<NotificationRow>(new CommandDefinition("""
            select id, type, title, body, deep_link, request_id, created_at_utc, read_at_utc, delivered_at_utc
              from notification_history
             where user_id = @userId
             order by created_at_utc desc
             limit @limit offset @offset
            """,
            new { userId, limit = pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from notification_history where user_id = @userId",
            new { userId }, cancellationToken: ct));

        var items = rows.Select(r => new NotificationDto(
            r.Id, r.Type, r.Title, r.Body, r.Deep_link, r.Request_id,
            r.Created_at_utc, r.Read_at_utc, r.Delivered_at_utc)).ToList();

        return new PagedResult<NotificationDto>(items, total, page, pageSize);
    }

    private sealed class NotificationRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Body { get; init; } = null!;
        public string? Deep_link { get; init; }
        public Guid? Request_id { get; init; }
        public DateTimeOffset Created_at_utc { get; init; }
        public DateTimeOffset? Read_at_utc { get; init; }
        public DateTimeOffset? Delivered_at_utc { get; init; }
    }

    public async Task<UnreadCountDto> UnreadCountAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*) from notification_history where user_id = @userId and read_at_utc is null
            """,
            new { userId }, cancellationToken: ct));
        return new UnreadCountDto(count);
    }

    /// <summary>Marks notifications read — only rows owned by the caller.</summary>
    public async Task<int> MarkReadAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("""
            update notification_history
               set read_at_utc = now()
             where user_id = @userId and read_at_utc is null and id = any(@ids)
            """,
            new { userId, ids = ids.ToArray() },
            cancellationToken: ct));
    }
}
