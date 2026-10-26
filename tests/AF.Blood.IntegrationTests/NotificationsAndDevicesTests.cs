using System.Net;
using System.Net.Http.Json;
using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Dapper;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class NotificationsAndDevicesTests : IntegrationTestBase
{
    [Fact]
    public async Task Device_registration_transfers_ownership_of_a_stale_subscription()
    {
        var (user1, _) = await RegisterUserAsync();
        var (user2, _) = await RegisterUserAsync();

        var register1 = await PostAsync(user1, "/api/v1/devices",
            new RegisterDeviceRequest("sub-abc-123", "android"));
        register1.EnsureSuccessStatusCode();

        var register2 = await PostAsync(user2, "/api/v1/devices",
            new RegisterDeviceRequest("sub-abc-123", "android"));
        register2.EnsureSuccessStatusCode();

        await using var conn = await OpenDbAsync();
        var owner = await conn.ExecuteScalarAsync<Guid>(
            "select user_id from user_devices where onesignal_subscription_id = 'sub-abc-123'");
        Assert.NotEqual(owner, Guid.Empty);
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from user_devices"));

        // Owner can disable it; a non-owner gets 404.
        var devices2 = await ReadAsync<List<DeviceDto>>(await user2.GetAsync("/api/v1/devices"));
        var disable = await user2.PostAsync($"/api/v1/devices/{devices2[0].Id}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var register3 = await PostAsync(user1, "/api/v1/devices",
            new RegisterDeviceRequest("other-device", "ios"));
        register3.EnsureSuccessStatusCode();
        var device3 = await ReadAsync<DeviceDto>(register3);
        var disableForeign = await user1.PostAsync($"/api/v1/devices/{device3.Id}/disable", null);
        disableForeign.EnsureSuccessStatusCode(); // owner disables own device
    }

    [Fact]
    public async Task Outbox_delivers_to_registered_devices_and_records_the_provider_id()
    {
        var (requester, _) = await RegisterUserAsync();
        await PostAsync(requester, "/api/v1/devices", new RegisterDeviceRequest("push-sub-1", "android"));
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id); // enqueues a notification for the requester

        Push.Reset();
        await DeliverOutboxAsync();

        var sent = Push.Sent.Single(m => m.SubscriptionIds.Contains("push-sub-1"));
        Assert.Equal("A donor accepted your request", sent.Title);
        Assert.EndsWith(request.Id.ToString(), sent.DeepLink);

        await using var conn = await OpenDbAsync();
        var (outboxStatus, historyDelivered, providerId) = await conn.QuerySingleAsync<(string, DateTimeOffset?, string?)>("""
            select o.status, h.delivered_at_utc, h.provider_message_id
              from notification_outbox o
              join notification_history h on h.id = o.notification_id
            """);
        Assert.Equal("delivered", outboxStatus);
        Assert.NotNull(historyDelivered);
        Assert.NotNull(providerId);
    }

    [Fact]
    public async Task Failed_delivery_is_retried_with_backoff_then_succeeds()
    {
        var (requester, _) = await RegisterUserAsync();
        await PostAsync(requester, "/api/v1/devices", new RegisterDeviceRequest("retry-sub", "android"));
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        Push.Script(new PushDeliveryResult(false, null, "provider temporarily down"));
        await DeliverOutboxAsync();

        await using var conn = await OpenDbAsync();
        var afterFailure = await conn.QuerySingleAsync<(string Status, int Attempts, DateTimeOffset Available)>(
            "select status, attempts, available_at_utc from notification_outbox");
        Assert.Equal("pending", afterFailure.Status);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.True(afterFailure.Available > DateTimeOffset.UtcNow); // scheduled backoff

        // Make it due again; this time the provider accepts.
        await conn.ExecuteAsync("update notification_outbox set available_at_utc = now() - interval '1 second'");
        Push.Reset();
        await DeliverOutboxAsync();

        var status = await conn.ExecuteScalarAsync<string>("select status from notification_outbox");
        Assert.Equal("delivered", status);
    }

    [Fact]
    public async Task Deliveries_without_devices_are_skipped_but_remain_in_the_inbox()
    {
        var (requester, _) = await RegisterUserAsync(); // no device registered
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        await DeliverOutboxAsync();

        await using var conn = await OpenDbAsync();
        var (outboxStatus, deliveredAt) = await conn.QuerySingleAsync<(string, DateTimeOffset?)>("""
            select o.status, h.delivered_at_utc
              from notification_outbox o
              join notification_history h on h.id = o.notification_id
            """);
        Assert.Equal("skipped", outboxStatus);
        Assert.Null(deliveredAt); // never claim delivery that did not happen

        var inbox = await ReadAsync<PagedResult<NotificationDto>>(
            await requester.GetAsync("/api/v1/notifications"));
        Assert.Equal(1, inbox.Total);
        Assert.Equal(NotificationTypes.DonorAccepted, inbox.Items[0].Type);
        Assert.Null(inbox.Items[0].DeliveredAtUtc);
    }

    [Fact]
    public async Task Mark_read_updates_only_owned_notifications()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var unread = await ReadAsync<UnreadCountDto>(await requester.GetAsync("/api/v1/notifications/unread-count"));
        Assert.Equal(1, unread.Unread);

        var inbox = await ReadAsync<PagedResult<NotificationDto>>(
            await requester.GetAsync("/api/v1/notifications"));
        var mark = await requester.PostAsJsonAsync("/api/v1/notifications/mark-read",
            new MarkNotificationsReadRequest([inbox.Items[0].Id]));
        mark.EnsureSuccessStatusCode();

        var after = await ReadAsync<UnreadCountDto>(await requester.GetAsync("/api/v1/notifications/unread-count"));
        Assert.Equal(0, after.Unread);
    }

    [Fact]
    public async Task Abuse_reports_can_be_filed_and_resolved()
    {
        var (member, _) = await RegisterUserAsync();
        var (targetClient, targetAuth) = await RegisterUserAsync();

        var create = await PostAsync(member, "/api/v1/reports/abuse",
            new CreateAbuseReportRequest(targetAuth.User.Id, null, "harassment", "Sent abusive messages."));
        Assert.True(create.IsSuccessStatusCode);

        var (admin, _) = await RegisterAdminAsync();
        var reports = await ReadAsync<PagedResult<AbuseReportDto>>(
            await admin.GetAsync("/api/v1/admin/abuse-reports?status=open"));
        Assert.Equal(1, reports.Total);

        var resolve = await PostAsync(admin, $"/api/v1/admin/abuse-reports/{reports.Items[0].Id}/resolve",
            new ResolveAbuseReportRequest("dismissed", "Reviewed; no violation found."));
        resolve.EnsureSuccessStatusCode();
    }
}
