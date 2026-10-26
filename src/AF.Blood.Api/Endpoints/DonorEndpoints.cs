using AF.Blood.Application.Abstractions;
using System.Security.Claims;
using AF.Blood.Api.Middleware;
using AF.Blood.Application.Features.Donors;
using AF.Blood.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace AF.Blood.Api.Endpoints;

public static class DonorEndpoints
{
    public static IEndpointRouteBuilder MapDonorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/donors").WithTags("Donors").RequireAuthorization();

        group.MapPut("/me", async Task<IResult> (
            UpsertDonorProfileRequest request,
            DonorProfileService donors,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var profile = await donors.UpsertAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Ok(profile);
        }).WithSummary("Create or update the caller's donor profile");

        group.MapGet("/me", async Task<IResult> (
            DonorProfileService donors,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var profile = await donors.GetAsync(principal.GetUserId(), ct)
                ?? throw AppException.NotFound("No donor profile yet.");
            return Results.Ok(profile);
        }).WithSummary("The caller's donor profile");

        group.MapPut("/me/consent", async Task<IResult> (
            DonorConsentRequest request,
            DonorProfileService donors,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var profile = await donors.SetConsentAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Ok(profile);
        }).WithSummary("Grant or withdraw discovery consent");

        group.MapPut("/me/availability", async Task<IResult> (
            DonorAvailabilityRequest request,
            DonorProfileService donors,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            var profile = await donors.SetAvailabilityAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.Ok(profile);
        }).WithSummary("Toggle availability for matching");

        group.MapGet("/me/eligibility", async Task<IResult> (
            DonorProfileService donors,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var eligibility = await donors.GetEligibilityAsync(principal.GetUserId(), ct);
            return Results.Ok(eligibility);
        }).WithSummary("Current matching eligibility and deferral countdown");

        return app;
    }
}
