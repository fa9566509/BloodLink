using System.Security.Cryptography;
using System.Text;
using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Moderation;
using AF.Blood.Contracts;
using AF.Blood.Infrastructure.Identity;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Identity = Microsoft.AspNetCore.Identity;

namespace AF.Blood.Infrastructure.Auth;

/// <summary>Registration, login, session refresh, verification and recovery.
/// Password hashing, lockout and normalization are delegated to ASP.NET Core
/// Identity; sessions are managed by <see cref="RefreshTokenService"/>.</summary>
public sealed class AuthService(
    UserManager<AppUser> userManager,
    RefreshTokenService refreshTokens,
    JwtTokenService jwt,
    ModerationService moderation,
    NpgsqlDataSource db,
    IVerificationCodeSender codeSender,
    IClock clock,
    AuditWriter audit,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger)
{
    private const int CodeTtlMinutes = 10;
    private const int MaxCodeAttempts = 5;

    // ---------------------------------------------------------------- sessions

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? correlationId, CancellationToken ct)
    {
        ValidateDisplayName(request.DisplayName);

        var user = new AppUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw AppException.Invalid(string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        await audit.WriteViaDataSourceAsync(db, user.Id, "account.registered", "user",
            user.Id.ToString(), new { email = user.Email }, correlationId, ct);

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? correlationId, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Constant-ish response: do not reveal whether the account exists.
            throw AppException.Unauthorized("Invalid email or password.");
        }

        if (await moderation.IsSuspendedAsync(user.Id, ct))
        {
            throw AppException.Forbidden("This account is suspended. Contact support.");
        }

        var valid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!valid)
        {
            var accessResult = await userManager.AccessFailedAsync(user);
            logger.LogInformation("Failed login for account {UserId} (lockout applied: {Locked})",
                user.Id, accessResult.Succeeded && await userManager.IsLockedOutAsync(user));
            throw AppException.Unauthorized("Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        await audit.WriteViaDataSourceAsync(db, user.Id, "account.login", "user",
            user.Id.ToString(), new { }, correlationId, ct);

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var result = await refreshTokens.ValidateAndRotateAsync(refreshToken, ct);
        var user = await userManager.FindByIdAsync(result.UserId.ToString())
            ?? throw AppException.Unauthorized("Account no longer exists.");

        if (await moderation.IsSuspendedAsync(user.Id, ct))
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id, "account suspended", ct);
            throw AppException.Forbidden("This account is suspended. Contact support.");
        }

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task LogoutAsync(string refreshToken, Guid userId, string? correlationId, CancellationToken ct)
    {
        await refreshTokens.RevokeAsync(refreshToken, "logout", ct);
        await audit.WriteViaDataSourceAsync(db, userId, "account.logout", "user",
            userId.ToString(), new { }, correlationId, ct);
    }

    public async Task<AuthResponse> BuildAuthResponseAsync(AppUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (token, expires) = jwt.CreateAccessToken(user.Id, user.Email!, user.DisplayName, roles.ToList());
        var (refresh, _) = await refreshTokens.IssueAsync(user.Id, ct);

        return new AuthResponse(token, refresh, expires,
            new UserDto(user.Id, user.Email!, user.DisplayName, user.PhoneNumber,
                user.EmailConfirmed, roles.ToList()));
    }

    // ------------------------------------------------------- verification/recovery

    public async Task SendVerificationCodeAsync(
        Guid userId, string purpose, string? correlationId, CancellationToken ct)
    {
        var (dbPurpose, destination, user) = await ResolvePurposeAsync(userId, purpose, ct);

        await using var conn = await db.OpenConnectionAsync(ct);
        var recent = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (
                select 1 from verification_codes
                 where user_id = @userId and purpose = @purpose
                   and created_at_utc > now() - interval '60 seconds')
            """,
            new { userId, purpose = dbPurpose }, cancellationToken: ct));

        if (recent)
        {
            throw AppException.Conflict("A code was just sent. Wait a minute before requesting another.");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        await conn.ExecuteAsync(new CommandDefinition("""
            insert into verification_codes (user_id, purpose, code_hash, expires_at_utc)
            values (@userId, @purpose, @hash, now() + make_interval(mins => @ttl))
            """,
            new { userId, purpose = dbPurpose, hash = Hash(code), ttl = CodeTtlMinutes },
            cancellationToken: ct));

        await codeSender.SendAsync(MapPurpose(dbPurpose), destination, code, ct);

        await audit.WriteAsync(conn, null, userId, "verification.code_sent", "user",
            userId.ToString(), new { purpose = dbPurpose }, correlationId, ct);
    }

    public async Task ConfirmVerificationAsync(
        Guid userId, string purpose, string code, string? correlationId, CancellationToken ct)
    {
        var (dbPurpose, _, user) = await ResolvePurposeAsync(userId, purpose, ct);

        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Hash, DateTimeOffset Expires, int Attempts)>(
            new CommandDefinition("""
                select id, code_hash as hash, expires_at_utc as expires, attempts
                  from verification_codes
                 where user_id = @userId and purpose = @purpose and consumed_at_utc is null
                 order by created_at_utc desc limit 1
                """,
                new { userId, purpose = dbPurpose }, cancellationToken: ct));

        if (row == default)
        {
            throw AppException.Invalid("No pending verification code. Request a new one.");
        }

        if (row.Attempts >= MaxCodeAttempts)
        {
            throw AppException.Conflict("Too many attempts. Request a new code.");
        }

        if (row.Expires <= clock.UtcNow || row.Hash != Hash(code.Trim()))
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "update verification_codes set attempts = attempts + 1 where id = @id",
                new { id = row.Id }, cancellationToken: ct));
            throw AppException.Invalid("The code is incorrect or has expired.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "update verification_codes set consumed_at_utc = now() where id = @id",
            new { id = row.Id }, cancellationToken: ct));

        if (dbPurpose == "email_verification")
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }
        else if (dbPurpose == "phone_verification")
        {
            user.PhoneNumberConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        await audit.WriteAsync(conn, null, userId, "verification.confirmed", "user",
            userId.ToString(), new { purpose = dbPurpose }, correlationId, ct);
    }

    /// <summary>Always succeeds from the caller's perspective — no account
    /// enumeration. Codes are only sent to existing accounts.</summary>
    public async Task ForgotPasswordAsync(string email, string? correlationId, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return;
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        var recent = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists (
                select 1 from verification_codes
                 where user_id = @userId and purpose = 'password_reset'
                   and created_at_utc > now() - interval '60 seconds')
            """,
            new { userId = user.Id }, cancellationToken: ct));

        if (recent)
        {
            return; // silently rate-limit without revealing account existence
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into verification_codes (user_id, purpose, code_hash, expires_at_utc)
            values (@userId, 'password_reset', @hash, now() + make_interval(mins => @ttl))
            """,
            new { userId = user.Id, hash = Hash(code), ttl = CodeTtlMinutes }, cancellationToken: ct));

        await codeSender.SendAsync(VerificationPurpose.PasswordReset, user.Email!, code, ct);

        await audit.WriteAsync(conn, null, user.Id, "account.password_reset_requested", "user",
            user.Id.ToString(), new { }, correlationId, ct);
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword, string? correlationId, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email.Trim())
            ?? throw AppException.Invalid("The code is incorrect or has expired.");

        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Hash, DateTimeOffset Expires, int Attempts)>(
            new CommandDefinition("""
                select id, code_hash as hash, expires_at_utc as expires, attempts
                  from verification_codes
                 where user_id = @userId and purpose = 'password_reset' and consumed_at_utc is null
                 order by created_at_utc desc limit 1
                """,
                new { userId = user.Id }, cancellationToken: ct));

        if (row == default || row.Attempts >= MaxCodeAttempts || row.Expires <= clock.UtcNow || row.Hash != Hash(code.Trim()))
        {
            if (row != default)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "update verification_codes set attempts = attempts + 1 where id = @id",
                    new { id = row.Id }, cancellationToken: ct));
            }

            throw AppException.Invalid("The code is incorrect or has expired.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "update verification_codes set consumed_at_utc = now() where id = @id",
            new { id = row.Id }, cancellationToken: ct));

        var result = await userManager.RemovePasswordAsync(user);
        if (!result.Succeeded)
        {
            throw AppException.Invalid(string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        result = await userManager.AddPasswordAsync(user, newPassword);
        if (!result.Succeeded)
        {
            throw AppException.Invalid(string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        await userManager.UpdateSecurityStampAsync(user);
        await refreshTokens.RevokeAllForUserAsync(user.Id, "password reset", ct);

        await audit.WriteAsync(conn, null, user.Id, "account.password_reset", "user",
            user.Id.ToString(), new { }, correlationId, ct);
    }

    public async Task ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, string? correlationId, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw AppException.NotFound("Account not found.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw AppException.Invalid(string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        await refreshTokens.RevokeAllForUserAsync(userId, "password changed", ct);
        await audit.WriteViaDataSourceAsync(db, userId, "account.password_changed", "user",
            userId.ToString(), new { }, correlationId, ct);
    }

    /// <summary>First-run bootstrap: while the configured key is set and no
    /// administrator exists yet, promotes the caller. Remove the key afterwards.</summary>
    public async Task BootstrapAdminAsync(Guid userId, string key, string? correlationId, CancellationToken ct)
    {
        var expected = jwtOptions.Value.BootstrapKey;
        if (string.IsNullOrWhiteSpace(expected) || key != expected)
        {
            throw AppException.Forbidden("Invalid bootstrap key.");
        }

        var admins = await userManager.GetUsersInRoleAsync("administrator");
        if (admins.Count > 0)
        {
            throw AppException.Conflict("An administrator already exists; bootstrap is closed.");
        }

        var result = await userManager.AddToRoleAsync((await userManager.FindByIdAsync(userId.ToString()))!, "administrator");
        if (!result.Succeeded)
        {
            throw AppException.Invalid(string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        await audit.WriteViaDataSourceAsync(db, userId, "account.bootstrap_admin", "user",
            userId.ToString(), new { }, correlationId, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(string DbPurpose, string Destination, AppUser User)> ResolvePurposeAsync(
        Guid userId, string purpose, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw AppException.NotFound("Account not found.");

        switch (purpose)
        {
            case "email_verification" when !string.IsNullOrWhiteSpace(user.Email):
                return ("email_verification", user.Email, user);
            case "phone_verification" when !string.IsNullOrWhiteSpace(user.PhoneNumber):
                return ("phone_verification", user.PhoneNumber, user);
            default:
                throw AppException.Invalid("Unknown verification purpose or missing destination.");
        }
    }

    private static VerificationPurpose MapPurpose(string dbPurpose) => dbPurpose switch
    {
        "email_verification" => VerificationPurpose.EmailVerification,
        "phone_verification" => VerificationPurpose.PhoneVerification,
        _ => VerificationPurpose.PasswordReset,
    };

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length is < 2 or > 128)
        {
            throw AppException.Invalid("Display name must be between 2 and 128 characters.");
        }
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
