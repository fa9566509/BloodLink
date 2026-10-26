using AF.Blood.Application.Abstractions;
using System.Security.Claims;
using AF.Blood.Api.Middleware;
using AF.Blood.Application.Features.Donations;
using AF.Blood.Application.Features.Matching;
using AF.Blood.Application.Features.Requests;
using AF.Blood.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AF.Blood.Api.Endpoints;

public static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/requests").WithTags("Requests").RequireAuthorization();
        var responsesGroup = app.MapGroup("/responses").WithTags("Requests").RequireAuthorization();
        var donationsGroup = app.MapGroup("/donations").WithTags("Donations").RequireAuthorization();

        group.MapPost("", async Task<IResult> (
            CreateRequestRequest request,
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var created = await requests.CreateAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Created($"/api/v1/requests/{created.Id}", created);
        }).WithSummary("Create a whole-blood request (schedules the first matching wave)");

        group.MapGet("/nearby", async Task<IResult> (
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await requests.BrowseNearbyAsync(principal.GetUserId(), page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("Open requests near the caller that their blood group can help");

        group.MapGet("/mine", async Task<IResult> (
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await requests.ListMineAsync(principal.GetUserId(), page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("Requests created by the caller");

        group.MapGet("/{id:guid}", async Task<IResult> (
            Guid id,
            RequestService requests,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var request = await requests.GetAsync(id, principal.GetUserId(), ct)
                ?? throw AppException.NotFound("Request not found.");
            return Results.Ok(request);
        }).WithSummary("Request details");

        group.MapGet("/{id:guid}/responses", [Authorize(Roles = "administrator,moderator")] async Task<IResult> (
            Guid id,
            RequestService requests,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await requests.ListResponsesAsync(id, page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("All responses to a request (moderators/administrators)");

        responsesGroup.MapGet("/mine", async Task<IResult> (
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await requests.ListMyResponsesAsync(principal.GetUserId(), page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("The caller's past responses");

        group.MapPost("/{id:guid}/responses", async Task<IResult> (
            Guid id,
            RespondToRequestRequest request,
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await requests.RespondAsync(principal.GetUserId(), id, request.Response, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Accept or decline a request");

        group.MapPost("/{id:guid}/cancel", async Task<IResult> (
            Guid id,
            RequestService requests,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await requests.CancelAsync(principal.GetUserId(), id, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Cancel an owned request");

        group.MapPost("/{id:guid}/donation-report", async Task<IResult> (
            Guid id,
            DonationReportRequest request,
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var record = await donations.ReportAsync(principal.GetUserId(), id, request, http.GetCorrelationId(), ct);
            return Results.Created($"/api/v1/donations/{record.Id}", record);
        }).WithSummary("Report a donation outcome (the accepting donor only)");

        group.MapPost("/{id:guid}/donation-acknowledgement", async Task<IResult> (
            Guid id,
            AcknowledgementRequest request,
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var record = await donations.AcknowledgeAsync(principal.GetUserId(), id, request, http.GetCorrelationId(), ct);
            return Results.Ok(record);
        }).WithSummary("Requester acknowledges or disputes the donation report");

        donationsGroup.MapGet("/mine", async Task<IResult> (
            DonationService donations,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var (page, pageSize) = http.GetPaging();
            var result = await donations.ListMineAsync(principal.GetUserId(), page, pageSize, ct);
            return Results.Ok(result);
        }).WithSummary("The caller's donation records");

        donationsGroup.MapGet("/{id:guid}", async Task<IResult> (
            Guid id,
            DonationService donations,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var record = await donations.GetAsync(id, ct)
                ?? throw AppException.NotFound("Donation record not found.");
            if (record.DonorUserId != principal.GetUserId() && !principal.IsInRole("administrator"))
            {
                throw AppException.Forbidden("Only the donor or an administrator can view this record.");
            }

            return Results.Ok(record);
        }).WithSummary("Donation record (owner or administrator)");

        return app;
    }
}
