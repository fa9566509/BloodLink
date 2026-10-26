using System.Net;
using System.Net.Http.Json;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class DonorProfileTests : IntegrationTestBase
{
    [Fact]
    public async Task Profile_upsert_stores_generalized_location_only()
    {
        var (client, auth) = await RegisterDonorAsync(lat: 31.520412, lon: 74.358712, bloodGroup: "O-");

        var profile = await client.GetFromJsonAsync<DonorProfileDto>("/api/v1/donors/me");
        Assert.NotNull(profile);
        Assert.Equal(auth.User.Id, profile!.UserId);
        Assert.Equal("O-", profile.BloodGroup);
        Assert.True(profile.IsDiscoverable);
        Assert.Equal(31.52, profile.ApproxLatitude); // 2 dp — precise value never stored (ADR-0005)
        Assert.Equal(74.36, profile.ApproxLongitude);
        Assert.Equal("2026-10-v1", profile.ConsentVersion);
        Assert.NotNull(profile.ConsentedAtUtc);
    }

    [Fact]
    public async Task Consent_withdrawal_clears_discovery_and_location()
    {
        var (client, _) = await RegisterDonorAsync(31.52, 74.35);

        var withdraw = await PutAsync(client, "/api/v1/donors/me/consent",
            new DonorConsentRequest(false, "2026-10-v1", null, null));
        withdraw.EnsureSuccessStatusCode();

        var profile = await client.GetFromJsonAsync<DonorProfileDto>("/api/v1/donors/me");
        Assert.False(profile!.IsDiscoverable);
        Assert.False(profile.IsAvailable);
        Assert.Null(profile.ConsentVersion);
        Assert.Null(profile.ApproxLatitude);
        Assert.Null(profile.ApproxLongitude);
    }

    [Fact]
    public async Task Eligibility_for_a_new_donor_has_no_deferral()
    {
        var (client, _) = await RegisterDonorAsync(31.52, 74.35);

        var eligibility = await client.GetFromJsonAsync<DonorEligibilityDto>("/api/v1/donors/me/eligibility");
        Assert.False(eligibility!.IsDeferred);
        Assert.Null(eligibility.EligibleAgainAtUtc);
        // Mandated safety wording (spec §11).
        Assert.Contains("donation centre", eligibility.Message);
    }

    [Fact]
    public async Task Invalid_blood_group_is_rejected()
    {
        var (client, _) = await RegisterUserAsync();
        var response = await PutAsync(client, "/api/v1/donors/me", new UpsertDonorProfileRequest("ZZ", 25));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discoverable_without_location_is_rejected()
    {
        var (client, _) = await RegisterUserAsync();
        await PutAsync(client, "/api/v1/donors/me", new UpsertDonorProfileRequest("O-", 25));

        var response = await PutAsync(client, "/api/v1/donors/me/consent",
            new DonorConsentRequest(true, "2026-10-v1", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
