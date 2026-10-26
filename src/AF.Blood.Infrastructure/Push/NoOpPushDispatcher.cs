using AF.Blood.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AF.Blood.Infrastructure.Push;

/// <summary>Used when OneSignal credentials are not configured (local dev, CI).
/// The outbox row completes so in-app history still works; nothing is claimed
/// to have been delivered to a real device.</summary>
public sealed class NoOpPushDispatcher(ILogger<NoOpPushDispatcher> logger) : IPushDispatcher
{
    public Task<PushDeliveryResult> DispatchAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Push (no-op, OneSignal not configured): notification {NotificationId} -> {Count} subscription(s): {Title}",
            message.NotificationId, message.SubscriptionIds.Count, message.Title);
        return Task.FromResult(new PushDeliveryResult(true, null, null));
    }
}
