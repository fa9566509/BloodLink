using AF.Blood.Application.Abstractions;
using System.Security.Claims;
using AF.Blood.Api.Middleware;
using AF.Blood.Contracts;
using AF.Blood.Infrastructure.Auth;
using AF.Blood.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace AF.Blood.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async Task<IResult> (
            RegisterRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await auth.RegisterAsync(request, http.GetCorrelationId(), ct);
            return Results.Ok(result);
        }).WithSummary("Register a new account").RequireRateLimiting("auth");

        group.MapPost("/login", async Task<IResult> (
            LoginRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request, http.GetCorrelationId(), ct);
            return Results.Ok(result);
        }).WithSummary("Log in with email and password").RequireRateLimiting("auth");

        group.MapPost("/refresh", async Task<IResult> (
            RefreshRequest request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request.RefreshToken, ct);
            return Results.Ok(result);
        }).WithSummary("Rotate a refresh token into a new access token").RequireRateLimiting("auth");

        group.MapPost("/logout", [Authorize] async Task<IResult> (
            RefreshRequest request,
            AuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.LogoutAsync(request.RefreshToken, principal.GetUserId(), http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Revoke the presented session");

        group.MapGet("/me", [Authorize] async Task<IResult> (
            UserManager<AppUser> userManager,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var user = await userManager.FindByIdAsync(principal.GetUserId().ToString())
                ?? throw AppException.NotFound("Account not found.");
            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new UserDto(user.Id, user.Email!, user.DisplayName,
                user.PhoneNumber, user.EmailConfirmed, roles.ToList()));
        }).WithSummary("Current account");

        group.MapPost("/verification/request", [Authorize] async Task<IResult> (
            SendVerificationRequest request,
            AuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.SendVerificationCodeAsync(principal.GetUserId(), request.Purpose, http.GetCorrelationId(), ct);
            return Results.Accepted();
        }).WithSummary("Request an email/phone verification code");

        group.MapPost("/verification/confirm", [Authorize] async Task<IResult> (
            ConfirmVerificationRequest request,
            AuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.ConfirmVerificationAsync(principal.GetUserId(), request.Purpose, request.Code, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Confirm an email/phone verification code");

        group.MapPost("/password/forgot", async Task<IResult> (
            ForgotPasswordRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.ForgotPasswordAsync(request.Email, http.GetCorrelationId(), ct);
            // Always the same response so accounts cannot be enumerated.
            return Results.Ok(new { message = "If the account exists, a reset code has been sent." });
        }).WithSummary("Request a password reset code").RequireRateLimiting("auth");

        group.MapPost("/password/reset", async Task<IResult> (
            ResetPasswordRequest request,
            AuthService auth,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.ResetPasswordAsync(request.Email, request.Code, request.NewPassword, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Reset the password with a recovery code").RequireRateLimiting("auth");

        group.MapPost("/password/change", [Authorize] async Task<IResult> (
            ChangePasswordRequest request,
            AuthService auth,
            ClaimsPrincipal principal,
            HttpContext http,
            CancellationToken ct) =>
        {
            await auth.ChangePasswordAsync(principal.GetUserId(), request, http.GetCorrelationId(), ct);
            return Results.NoContent();
        }).WithSummary("Change password (revokes all sessions)");

        return app;
    }
}
