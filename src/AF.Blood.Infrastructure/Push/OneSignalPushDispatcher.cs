using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AF.Blood.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AF.Blood.Infrastructure.Push;

public class OneSignalOptions
{
    public const string SectionName = "OneSignal";

    public string? AppId { get; set; }
    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ApiKey);
}

public record OneSignalEnvelope(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("include_subscription_ids")] IReadOnlyList<string> IncludeSubscriptionIds,
    [property: JsonPropertyName("headings")] IReadOnlyDictionary<string, string> Headings,
    [property: JsonPropertyName("contents")] IReadOnlyDictionary<string, string> Contents,
    [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string>? Data);

/// <summary>Delivers pushes through the OneSignal REST API
/// (POST https://api.onesignal.com/notifications). At-least-once semantics:
/// the caller retries on failure, OneSignal deduplicates repeat deliveries.</summary>
public sealed partial class OneSignalPushDispatcher(
    HttpClient http,
    IOptions<OneSignalOptions> options,
    ILogger<OneSignalPushDispatcher> logger) : IPushDispatcher
{
    private readonly OneSignalOptions _options = options.Value;

    public async Task<PushDeliveryResult> DispatchAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string>
        {
            ["notification_id"] = message.NotificationId.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(message.DeepLink))
        {
            data["deep_link"] = message.DeepLink;
        }

        var envelope = new OneSignalEnvelope(
            _options.AppId!,
            message.SubscriptionIds,
            new Dictionary<string, string> { ["en"] = message.Title },
            new Dictionary<string, string> { ["en"] = message.Body },
            data);

        using var response = await http.PostAsJsonAsync("notifications", envelope, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogDeliveryFailed(message.NotificationId, (int)response.StatusCode, body);
            return new PushDeliveryResult(false, null, $"HTTP {(int)response.StatusCode}");
        }

        var result = System.Text.Json.JsonSerializer.Deserialize<OneSignalResponse>(body);
        if (result?.Errors is { Count: > 0 })
        {
            LogDeliveryFailed(message.NotificationId, (int)response.StatusCode, body);
            return new PushDeliveryResult(false, null, string.Join(';', result.Errors));
        }

        return new PushDeliveryResult(true, result?.Id, null);
    }

    public record OneSignalResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("recipients")] int? Recipients,
        [property: JsonPropertyName("errors")] IReadOnlyList<string>? Errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OneSignal delivery failed for notification {NotificationId}: {Status} {Body}")]
    private partial void LogDeliveryFailed(Guid notificationId, int status, string body);
}
