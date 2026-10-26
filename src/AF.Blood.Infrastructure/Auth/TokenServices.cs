using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AF.Blood.Application.Abstractions;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace AF.Blood.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "af-foundation";
    public string Audience { get; set; } = "af-foundation-clients";

    /// <summary>HS256 signing key; MUST be >= 32 random characters. Supplied via
    /// configuration/environment in every environment (never committed).</summary>
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>First-run administrator bootstrap key. When set and no
    /// administrator exists yet, POST /api/v1/admin/bootstrap with this key
    /// promotes the caller. Remove it after creating the first admin.</summary>
    public string? BootstrapKey { get; set; }
}

/// <summary>Issues short-lived signed JWT access tokens (ADR-0002).</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(
        Guid userId, string email, string displayName, IReadOnlyCollection<string> roles)
    {
        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key is missing or shorter than 32 characters. Configure a strong signing key.");
        }

        var now = clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("name", displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public TokenValidationParameters ValidationParameters(bool validateLifetime = true) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
        ValidateLifetime = validateLifetime,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}

/// <summary>Opaque refresh tokens stored as SHA-256 hashes; rotation with reuse
/// detection (ADR-0002).</summary>
public sealed class RefreshTokenService(
    NpgsqlDataSource db, IOptions<JwtOptions> options, IClock clock)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<(string Token, DateTimeOffset ExpiresAtUtc)> IssueAsync(Guid userId, CancellationToken ct)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = clock.UtcNow.AddDays(_jwt.RefreshTokenDays);

        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            insert into auth_refresh_tokens (user_id, token_hash, expires_at_utc)
            values (@userId, @hash, @expires)
            """,
            new { userId, hash = Hash(token), expires }, cancellationToken: ct));

        return (token, expires);
    }

    public record RefreshResult(Guid UserId, string NewToken, DateTimeOffset NewExpiresAtUtc);

    /// <summary>Validates and rotates a refresh token. Presenting a token that
    /// was already rotated (possible theft) revokes ALL of the user's sessions.</summary>
    public async Task<RefreshResult> ValidateAndRotateAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<(Guid Id, Guid UserId, DateTimeOffset Expires, DateTimeOffset? Revoked)>(
            new CommandDefinition("""
                select id, user_id, expires_at_utc as expires, revoked_at_utc as revoked
                  from auth_refresh_tokens where token_hash = @hash for update
                """,
                new { hash }, tx, cancellationToken: ct));

        if (row == default)
        {
            throw AppException.NotFound("Session not recognised.");
        }

        if (row.Revoked is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                update auth_refresh_tokens
                   set revoked_at_utc = now(), revoked_reason = 'refresh token reuse detected'
                 where user_id = @userId and revoked_at_utc is null
                """,
                new { row.UserId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);

            // A suspension explains the revoked session more precisely than
            // generic token theft — surface 403 in that case.
            var suspended = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists (select 1 from account_suspensions
                                where user_id = @userId and lifted_at_utc is null)
                """,
                new { row.UserId }, cancellationToken: ct));
            throw suspended
                ? AppException.Forbidden("This account is suspended. Contact support.")
                : AppException.Unauthorized("Session token was already used; all sessions were revoked.");
        }

        if (row.Expires <= clock.UtcNow)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "update auth_refresh_tokens set revoked_at_utc = now(), revoked_reason = 'expired' where id = @id",
                new { id = row.Id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            throw AppException.Unauthorized("Session expired.");
        }

        var newToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var newExpires = clock.UtcNow.AddDays(_jwt.RefreshTokenDays);

        var newId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            insert into auth_refresh_tokens (user_id, token_hash, expires_at_utc)
            values (@userId, @hash, @newExpires) returning id
            """,
            new { userId = row.UserId, hash = Hash(newToken), newExpires }, tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition("""
            update auth_refresh_tokens
               set revoked_at_utc = now(), revoked_reason = 'rotated', replaced_by_token_id = @newId
             where id = @id
            """,
            new { id = row.Id, newId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new RefreshResult(row.UserId, newToken, newExpires);
    }

    public async Task RevokeAsync(string token, string reason, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            update auth_refresh_tokens
               set revoked_at_utc = now(), revoked_reason = @reason
             where token_hash = @hash and revoked_at_utc is null
            """,
            new { reason, hash = Hash(token) }, cancellationToken: ct));
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            update auth_refresh_tokens
               set revoked_at_utc = now(), revoked_reason = @reason
             where user_id = @userId and revoked_at_utc is null
            """,
            new { userId, reason }, cancellationToken: ct));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
