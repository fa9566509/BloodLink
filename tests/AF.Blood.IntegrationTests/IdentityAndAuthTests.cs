using System.Net;
using System.Net.Http.Json;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Dapper;
using AF.Blood.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class IdentityAndAuthTests : IntegrationTestBase
{
    [Fact]
    public async Task Identity_works_against_the_flyway_created_schema()
    {
        // Proves V001 matches what EF Core Identity expects (ADR-0003).
        var userManager = UserManager();
        var user = new AppUser
        {
            UserName = "schema-check@example.test",
            Email = "schema-check@example.test",
            DisplayName = "Schema Check",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var createResult = await userManager.CreateAsync(user, "Sup3rSecret!x");
        Assert.True(createResult.Succeeded, string.Join(';', createResult.Errors.Select(e => e.Description)));

        var found = await userManager.FindByEmailAsync("SCHEMA-CHECK@EXAMPLE.TEST");
        Assert.NotNull(found);
        Assert.Equal("Schema Check", found!.DisplayName);

        // Roles seeded by V003 exist for Identity's role store.
        await using var conn = await OpenDbAsync();
        var adminRole = await conn.ExecuteScalarAsync<int>(
            "select count(*) from \"AspNetRoles\" where \"NormalizedName\" = 'ADMINISTRATOR'");
        Assert.Equal(1, adminRole);
    }

    [Fact]
    public async Task Register_login_and_me_roundtrip()
    {
        var (client, auth) = await RegisterUserAsync();

        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/auth/me");
        Assert.Equal(auth.User.Id, me!.Id);
        Assert.Equal(auth.User.Email, me.Email);
        Assert.False(me.EmailConfirmed);

        // Wrong password -> 401 (no account enumeration difference)
        var anon = Client();
        var badLogin = await PostAsync(anon, "/api/v1/auth/login",
            new LoginRequest(auth.User.Email, "WrongPassword!1"));
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var goodLogin = await PostAsync(anon, "/api/v1/auth/login",
            new LoginRequest(auth.User.Email, "Sup3rSecret!x"));
        Assert.Equal(HttpStatusCode.OK, goodLogin.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotation_and_reuse_detection()
    {
        var (_, auth) = await RegisterUserAsync();
        var anon = Client();

        var refresh1 = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refresh1.StatusCode);
        var rotated = await ReadAsync<AuthResponse>(refresh1);

        // Reusing the rotated token is treated as theft: all sessions revoked.
        var refresh2 = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh2.StatusCode);

        var refresh3 = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh3.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        var (client, auth) = await RegisterUserAsync();
        var anon = Client();

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Email_verification_flow_confirms_the_account()
    {
        var (client, auth) = await RegisterUserAsync();

        var request = await PostAsync(client, "/api/v1/auth/verification/request",
            new SendVerificationRequest("email_verification"));
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);

        var code = Codes.LastCodeFor("EmailVerification", auth.User.Email);
        Assert.NotNull(code);

        var confirm = await PostAsync(client, "/api/v1/auth/verification/confirm",
            new ConfirmVerificationRequest("email_verification", code!));
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/auth/me");
        Assert.True(me!.EmailConfirmed);
    }

    [Fact]
    public async Task Password_reset_revokes_sessions_and_allows_new_password()
    {
        var (client, auth) = await RegisterUserAsync();
        var anon = Client();

        await PostAsync(anon, "/api/v1/auth/password/forgot", new ForgotPasswordRequest(auth.User.Email));
        var code = Codes.LastCodeFor("PasswordReset", auth.User.Email);
        Assert.NotNull(code);

        var reset = await PostAsync(anon, "/api/v1/auth/password/reset",
            new ResetPasswordRequest(auth.User.Email, code!, "N3wSup3rSecret!"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Old session no longer usable.
        var oldRefresh = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);

        var login = await PostAsync(anon, "/api/v1/auth/login",
            new LoginRequest(auth.User.Email, "N3wSup3rSecret!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_promotes_the_first_admin_only()
    {
        var (admin, _) = await RegisterAdminAsync();

        var me = await admin.GetFromJsonAsync<UserDto>("/api/v1/auth/me");
        Assert.Contains("administrator", me!.Roles);

        var second = await RegisterUserAsync();
        var bootstrap = await PostAsync(second.Client, "/api/v1/admin/bootstrap", new AdminBootstrapRequest("dev-bootstrap-key"));
        Assert.Equal(HttpStatusCode.Conflict, bootstrap.StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_weak_passwords()
    {
        var anon = Client();
        var response = await PostAsync(anon, "/api/v1/auth/register",
            new RegisterRequest("weak@example.test", "short", "Weak User", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
