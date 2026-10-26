using System.Net;
using System.Net.Http.Json;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Dapper;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class AuthorizationAndPrivacyTests : IntegrationTestBase
{
    [Fact]
    public async Task Anonymous_callers_get_problem_details_401()
    {
        var anon = Client();
        var response = await anon.GetAsync("/api/v1/donors/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Members_cannot_reach_admin_endpoints()
    {
        var (member, _) = await RegisterUserAsync();
        var response = await member.GetAsync("/api/v1/admin/donations");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Moderators_and_verifiers_get_scoped_access()
    {
        var (admin, _) = await RegisterAdminAsync();

        // Administrator can review donations and see abuse reports.
        var donations = await admin.GetAsync("/api/v1/admin/donations");
        Assert.Equal(HttpStatusCode.OK, donations.StatusCode);
        var policies = await admin.GetAsync("/api/v1/admin/policies");
        Assert.Equal(HttpStatusCode.OK, policies.StatusCode);
    }

    [Fact]
    public async Task Suspended_accounts_cannot_log_in()
    {
        var (admin, _) = await RegisterAdminAsync();
        var (member, auth) = await RegisterUserAsync();

        var suspend = await PostAsync(admin, $"/api/v1/admin/users/{auth.User.Id}/suspend",
            new SuspendUserRequest("Abusive behaviour."));
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        var anon = Client();
        var login = await PostAsync(anon, "/api/v1/auth/login", new LoginRequest(auth.User.Email, "Sup3rSecret!x"));
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var restore = await PostAsync(admin, $"/api/v1/admin/users/{auth.User.Id}/restore",
            new SuspendUserRequest("Appeal accepted."));
        restore.EnsureSuccessStatusCode();

        var reLogin = await PostAsync(anon, "/api/v1/auth/login", new LoginRequest(auth.User.Email, "Sup3rSecret!x"));
        Assert.Equal(HttpStatusCode.OK, reLogin.StatusCode);
    }

    [Fact]
    public async Task Suspension_revokes_existing_sessions()
    {
        var (admin, _) = await RegisterAdminAsync();
        var (member, auth) = await RegisterUserAsync();

        // Sessions work before suspension.
        var before = await member.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await PostAsync(admin, $"/api/v1/admin/users/{auth.User.Id}/suspend", new SuspendUserRequest("Spam."));
        var anon = Client();
        var refresh = await PostAsync(anon, "/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
    }

    [Fact]
    public async Task Administrators_cannot_be_suspended_through_the_api()
    {
        var (admin, _) = await RegisterAdminAsync();
        var me = await admin.GetFromJsonAsync<UserDto>("/api/v1/auth/me");

        var response = await PostAsync(admin, $"/api/v1/admin/users/{me!.Id}/suspend",
            new SuspendUserRequest("Self suspension attempt."));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Donation_records_are_visible_only_to_the_donor_or_admin()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var report = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        var record = await ReadAsync<DonationRecordDto>(report);

        var outsider = await RegisterUserAsync();
        var forbidden = await outsider.Client.GetAsync($"/api/v1/donations/{record.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var owner = await donor.GetAsync($"/api/v1/donations/{record.Id}");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Matching_output_never_contains_raw_candidate_locations()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (donorClient, donorAuth) = await RegisterDonorAsync(31.52123, 74.35678, "O-"); // precise input

        await RunMatchingAsync();

        await using var conn = await OpenDbAsync();
        // Only the generalized point exists in the database at all.
        var stored = await conn.ExecuteScalarAsync<string>(
            "select st_astext(location) from donor_profiles where user_id = @id",
            new { id = donorAuth.User.Id });
        Assert.Contains("31.52", stored);
        Assert.DoesNotContain("31.52123", stored);

        // The requester's view of the request carries no donor data whatsoever.
        var detail = await requester.GetFromJsonAsync<RequestDto>($"/api/v1/requests/{request.Id}");
        Assert.Null(detail!.MatchedDonorUserId);
    }

    [Fact]
    public async Task Sensitive_actions_are_audited()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        await using var conn = await OpenDbAsync();
        var actions = (await conn.QueryAsync<string>(
            "select action from audit_log order by created_at_utc")).ToList();

        Assert.Contains("account.registered", actions);
        Assert.Contains("request.created", actions);
        Assert.Contains("donor_profile.consent_granted", actions);
        Assert.Contains("request.accepted", actions);
    }
}
