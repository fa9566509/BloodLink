namespace AF.Blood.Contracts;

// --- Devices ------------------------------------------------------------------

public record RegisterDeviceRequest(string OneSignalSubscriptionId, string Platform); // android | ios | web
public record DeviceDto(Guid Id, string OneSignalSubscriptionId, string Platform, bool IsActive, DateTimeOffset CreatedAtUtc);

// --- Notifications ------------------------------------------------------------

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? DeepLink,
    Guid? RequestId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? DeliveredAtUtc);

public record MarkNotificationsReadRequest(IReadOnlyList<Guid> Ids);
public record UnreadCountDto(int Unread);
