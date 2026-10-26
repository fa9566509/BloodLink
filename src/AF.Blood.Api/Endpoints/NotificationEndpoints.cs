using System.Security.Claims;
using AF.Blood.Api.Middleware;
using AF.Blood.Application.Features.Devices;
using AF.Blood.Application.Features.Moderation;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace AF.Blood.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();
        var devices = app.MapGroup("/devices").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("", async Task<IResult> (
            NotificationService service,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await service.ListAsync(principal.GetUserId(), page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("Notification history (works even when push is unavailable)");

        notifications.MapGet("/unread-count", async Task<IResult> (
            NotificationService service,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var count = await service.UnreadCountAsync(principal.GetUserId(), ct);
            return Results.Ok(count);
        }).WithSummary("Unread notification count");

        notifications.MapPost("/mark-read", async Task<IResult> (
            MarkNotificationsReadRequest request,
            NotificationService service,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var updated = await service.MarkReadAsync(principal.GetUserId(), request.Ids, ct);
            return Results.Ok(new { updated });
        }).WithSummary("Mark owned notifications as read");

        devices.MapPost("", async Task<IResult> (
            RegisterDeviceRequest request,
            DeviceService devices,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var device = await devices.RegisterAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Ok(device);
        }).WithSummary("Register (or take over) a OneSignal subscription");

        devices.MapGet("", async Task<IResult> (
            DeviceService devices,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var list = await devices.ListMineAsync(principal.GetUserId(), ct);
            return Results.Ok(list);
        }).WithSummary("The caller's registered devices");

        devices.MapPost("/{id:guid}/disable", async Task<IResult> (
            Guid id,
            DeviceService devices,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await devices.DisableAsync(principal.GetUserId(), id, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Disable an owned device subscription");

        app.MapPost("/reports/abuse", [Authorize] async Task<IResult> (
            CreateAbuseReportRequest request,
            ModerationService moderation,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var report = await moderation.CreateAbuseReportAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Created($"/api/v1/reports/abuse/{report.Id}", report);
        }).WithSummary("Report abuse of a user or request");

        return app;
    }
}
