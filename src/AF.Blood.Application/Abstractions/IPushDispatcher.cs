namespace AF.Blood.Application.Abstractions;

/// <summary>A push notification ready for delivery to specific OneSignal
/// subscriptions. Payloads contain no credentials, no medical details, no
/// phone numbers and no precise coordinates.</summary>
public record PushMessage(
    IReadOnlyList<string> SubscriptionIds,
    string Title,
    string Body,
    string? DeepLink,
    Guid NotificationId);

public record PushDeliveryResult(bool Success, string? ProviderMessageId, string? Error);

/// <summary>Port to the external push provider. Implementations MUST tolerate
/// being called twice with the same message (at-least-once delivery).</summary>
public interface IPushDispatcher
{
    Task<PushDeliveryResult> DispatchAsync(PushMessage message, CancellationToken cancellationToken = default);
}
