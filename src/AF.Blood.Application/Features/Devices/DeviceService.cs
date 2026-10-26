using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Contracts;
using Dapper;
using Npgsql;

namespace AF.Blood.Application.Features.Devices;

public sealed class DeviceService(NpgsqlDataSource db, AuditWriter audit)
{
    private static readonly IReadOnlyList<string> Platforms = ["android", "ios", "web"];

    /// <summary>
    /// Registers (or re-registers) a OneSignal subscription for the caller.
    /// If the subscription was previously owned by another account, ownership is
    /// transferred to the newest registration and the transfer is audited —
    /// this is how stale device handovers are handled in v1.
    /// </summary>
    public async Task<DeviceDto> RegisterAsync(
        Guid userId, RegisterDeviceRequest request, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OneSignalSubscriptionId) || request.OneSignalSubscriptionId.Length > 256)
        {
            throw AppException.Invalid("OneSignal subscription id is required.");
        }

        if (!Platforms.Contains(request.Platform))
        {
            throw AppException.Invalid("Platform must be android, ios or web.");
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var previousOwner = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select user_id from user_devices where onesignal_subscription_id = @subscriptionId
            """,
            new { subscriptionId = request.OneSignalSubscriptionId }, tx, cancellationToken: ct));

        var id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into user_devices (user_id, onesignal_subscription_id, platform)
            values (@userId, @subscriptionId, @platform)
            on conflict (onesignal_subscription_id) do update
                set user_id = excluded.user_id,
                    platform = excluded.platform,
                    is_active = true,
                    updated_at_utc = now(),
                    last_registered_at_utc = now()
            returning id
            """,
            new { userId, subscriptionId = request.OneSignalSubscriptionId.Trim(), platform = request.Platform },
            tx, cancellationToken: ct));

        if (previousOwner is { } prev && prev != userId)
        {
            await audit.WriteAsync(conn, tx, userId, "device.ownership_transferred", "user_device",
                id.ToString(), new { previousOwner = prev, subscriptionId = request.OneSignalSubscriptionId },
                correlationId, ct);
        }

        await tx.CommitAsync(ct);

        var row = await conn.QuerySingleAsync<(Guid Id, string Subscription, string Platform, bool Active, DateTimeOffset Created)>(
            new CommandDefinition("""
                select id, onesignal_subscription_id as subscription, platform, is_active as active, created_at_utc as created
                  from user_devices where id = @id
                """,
                new { id }, cancellationToken: ct));

        return new DeviceDto(row.Id, row.Subscription, row.Platform, row.Active, row.Created);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListMineAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(Guid Id, string Subscription, string Platform, bool Active, DateTimeOffset Created)>(
            new CommandDefinition("""
                select id, onesignal_subscription_id as subscription, platform, is_active as active, created_at_utc as created
                  from user_devices where user_id = @userId
                 order by last_registered_at_utc desc
                """,
                new { userId }, cancellationToken: ct));

        return rows.Select(r => new DeviceDto(r.Id, r.Subscription, r.Platform, r.Active, r.Created)).ToList();
    }

    public async Task DisableAsync(Guid userId, Guid deviceId, string? correlationId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition("""
            update user_devices set is_active = false, updated_at_utc = now()
             where id = @deviceId and user_id = @userId and is_active
            """,
            new { deviceId, userId }, cancellationToken: ct));

        if (updated == 0)
        {
            throw AppException.NotFound("Device not found for this account.");
        }

        await audit.WriteAsync(conn, null, userId, "device.disabled", "user_device",
            deviceId.ToString(), new { }, correlationId, ct);
    }
}
