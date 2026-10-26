using System.Text.Json;
using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Audit;

public sealed class AuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Convenience for writing outside an existing transaction.</summary>
    public async Task WriteViaDataSourceAsync(
        NpgsqlDataSource dataSource,
        Guid? actorUserId,
        string action,
        string entityType,
        string? entityId,
        object? details,
        string? correlationId,
        CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await WriteAsync(conn, tx: null, actorUserId, action, entityType, entityId, details, correlationId, ct);
    }

    public Task WriteAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        Guid? actorUserId,
        string action,
        string entityType,
        string? entityId,
        object? details,
        string? correlationId,
        CancellationToken ct = default)
    {
        return conn.ExecuteAsync(new CommandDefinition("""
            insert into audit_log (actor_user_id, action, entity_type, entity_id, details, correlation_id)
            values (@actor, @action, @entityType, @entityId, @details::jsonb, @correlationId)
            """,
            new
            {
                actor = actorUserId,
                action,
                entityType,
                entityId,
                details = JsonSerializer.Serialize(details ?? new { }),
                correlationId,
            },
            tx,
            cancellationToken: ct));
    }
}
