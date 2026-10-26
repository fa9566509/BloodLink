using System.Security.Claims;
using AF.Blood.Api.Middleware;
using AF.Blood.Application.Features.Donations;
using AF.Blood.Application.Features.Moderation;
using AF.Blood.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AF.Blood.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization();

        // ---- bootstrap (first administrator) --------------------------------
        group.MapPost("/bootstrap", async Task<IResult> (
            [FromBody] AdminBootstrapRequest request,
            Infrastructure.Auth.AuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.BootstrapAdminAsync(principal.GetUserId(), request.Key, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("First-run: promote the caller to administrator with the configured bootstrap key")
          .RequireRateLimiting("auth");

        // ---- abuse reports ---------------------------------------------------
        group.MapGet("/abuse-reports", [Authorize(Roles = "administrator,moderator")] async Task<IResult> (
            ModerationService moderation,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var status = (string?)http.Request.Query["status"];
            var result = await moderation.ListAbuseReportsAsync(status, page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("List abuse reports");

        group.MapPost("/abuse-reports/{id:guid}/resolve", [Authorize(Roles = "administrator,moderator")] async Task<IResult> (
            Guid id,
            ResolveAbuseReportRequest request,
            ModerationService moderation,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var report = await moderation.ResolveAbuseReportAsync(principal.GetUserId(), id, request, http.GetCorrelationId(), ct);
            return Results.Ok(report);
        }).WithSummary("Resolve or dismiss an abuse report");

        // ---- suspensions -----------------------------------------------------
        group.MapPost("/users/{id:guid}/suspend", [Authorize(Roles = "administrator,moderator")] async Task<IResult> (
            Guid id,
            SuspendUserRequest request,
            ModerationService moderation,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await moderation.SuspendUserAsync(principal.GetUserId(), id, request.Reason, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Suspend an account");

        group.MapPost("/users/{id:guid}/restore", [Authorize(Roles = "administrator,moderator")] async Task<IResult> (
            Guid id,
            [FromBody] SuspendUserRequest? request,
            ModerationService moderation,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await moderation.RestoreUserAsync(principal.GetUserId(), id, request?.Reason, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Lift a suspension");

        // ---- donation moderation ----------------------------------------------
        group.MapGet("/donations", [Authorize(Roles = "administrator,moderator,donation_verifier")] async Task<IResult> (
            DonationService donations,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var status = (string?)http.Request.Query["status"];
            var result = await donations.ListForReviewAsync(status, page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("Donation records awaiting review (default: reported/disputed)");

        group.MapPost("/donations/{id:guid}/verify", [Authorize(Roles = "administrator,moderator,donation_verifier")] async Task<IResult> (
            Guid id,
            [FromBody] AdminDonationDecisionRequest request,
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var record = await donations.VerifyAsync(principal.GetUserId(), id, request.Note, http.GetCorrelationId(), ct);
            return Results.Ok(record);
        }).WithSummary("Verify a donation report (applies the approved deferral)");

        group.MapPost("/donations/{id:guid}/reject", [Authorize(Roles = "administrator,moderator,donation_verifier")] async Task<IResult> (
            Guid id,
            [FromBody] AdminDonationDecisionRequest request,
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var record = await donations.RejectAsync(principal.GetUserId(), id, request.Note, http.GetCorrelationId(), ct);
            return Results.Ok(record);
        }).WithSummary("Reject a donation report (removes provisional restrictions)");

        group.MapPost("/donations/{id:guid}/correct", [Authorize(Roles = "administrator")] async Task<IResult> (
            Guid id,
            [FromBody] AdminDonationDecisionRequest request,
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var record = await donations.CorrectAsync(principal.GetUserId(), id,
                request.CorrectedDonationDate, request.Note, http.GetCorrelationId(), ct);
            return Results.Ok(record);
        }).WithSummary("Administratively correct a donation record");

        // ---- deferral policies ------------------------------------------------
        group.MapGet("/policies", [Authorize(Roles = "administrator,moderator,donation_verifier")] async Task<IResult> (
            PolicyService policies,
            CancellationToken ct) =>
        {
            var list = await policies.ListAsync(ct);
            return Results.Ok(list);
        }).WithSummary("List deferral policy versions");

        group.MapPost("/policies", [Authorize(Roles = "administrator")] async Task<IResult> (
            CreatePolicyVersionRequest request,
            PolicyService policies,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var created = await policies.CreateAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Created($"/api/v1/admin/policies/{created.Id}", created);
        }).WithSummary("Create a deferral policy version (approval reference required)");

        group.MapPost("/policies/{id:guid}/activate", [Authorize(Roles = "administrator")] async Task<IResult> (
            Guid id,
            PolicyService policies,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var activated = await policies.ActivateAsync(principal.GetUserId(), id, http.GetCorrelationId(), ct);
            return Results.Ok(activated);
        }).WithSummary("Activate a policy version for new donation reports");

        return app;
    }
}
