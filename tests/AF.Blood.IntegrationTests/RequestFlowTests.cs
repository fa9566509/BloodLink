using System.Net;
using System.Net.Http.Json;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Dapper;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class RequestFlowTests : IntegrationTestBase
{
    [Fact]
    public async Task Creating_a_request_schedules_the_first_matching_wave()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);

        Assert.Equal(RequestStatuses.Open, request.Status);
        Assert.Equal(31.52, request.ApproxLatitude);
        Assert.Equal(74.35, request.ApproxLongitude);

        await using var conn = await OpenDbAsync();
        var run = await conn.QuerySingleAsync<(int Wave, int Radius, string Status)>(
            "select wave_index, radius_km, status from matching_runs where request_id = @id",
            new { id = request.Id });
        Assert.Equal((0, 25, "pending"), run);
    }

    [Fact]
    public async Task Concurrent_acceptance_allows_exactly_one_donor()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (donor1, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        var (donor2, _) = await RegisterDonorAsync(31.54, 74.34, "A+");

        var task1 = PostAsync(donor1, $"/api/v1/requests/{request.Id}/responses", new RespondToRequestRequest("accepted"));
        var task2 = PostAsync(donor2, $"/api/v1/requests/{request.Id}/responses", new RespondToRequestRequest("accepted"));
        var results = await Task.WhenAll(task1, task2);

        var accepted = results.Count(r => r.StatusCode == HttpStatusCode.NoContent);
        var conflicted = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, accepted);
        Assert.Equal(1, conflicted);

        await using var conn = await OpenDbAsync();
        var (status, matchedDonor) = await conn.QuerySingleAsync<(string Status, Guid? MatchedDonor)>(
            "select status, matched_donor_user_id from donation_requests where id = @id",
            new { id = request.Id });
        Assert.Equal("matched", status);
        Assert.NotNull(matchedDonor);

        var responseCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from request_responses where request_id = @id and response = 'accepted'",
            new { id = request.Id });
        Assert.Equal(1, responseCount);
    }

    [Fact]
    public async Task Duplicate_response_is_rejected()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36);

        var first = await PostAsync(donor, $"/api/v1/requests/{request.Id}/responses",
            new RespondToRequestRequest(ResponseTypes.Declined));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await PostAsync(donor, $"/api/v1/requests/{request.Id}/responses",
            new RespondToRequestRequest(ResponseTypes.Declined));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Acceptance_requires_consent_and_availability()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);

        // Donor with profile but no consent/availability.
        var (donorClient, _) = await RegisterUserAsync();
        var profilePut = await PutAsync(donorClient, "/api/v1/donors/me", new UpsertDonorProfileRequest("O-", 25));
        var profileBody = await profilePut.Content.ReadAsStringAsync();

        var response = await PostAsync(donorClient, $"/api/v1/requests/{request.Id}/responses",
            new RespondToRequestRequest(ResponseTypes.Accepted));
        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"profilePut={profilePut.StatusCode} body={profileBody} accept={response.StatusCode} acceptBody={await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Requester_cannot_respond_to_own_request()
    {
        var (requester, _) = await RegisterUserAsync();
        await PostAsync(requester, "/api/v1/donors/me", new UpsertDonorProfileRequest("O-", 25));
        var request = await CreateRequestAsync(requester);

        var response = await PostAsync(requester, $"/api/v1/requests/{request.Id}/responses",
            new RespondToRequestRequest(ResponseTypes.Accepted));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_cancel()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (other, _) = await RegisterUserAsync();

        var forbidden = await PostAsync(other, $"/api/v1/requests/{request.Id}/cancel", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await PostAsync(requester, $"/api/v1/requests/{request.Id}/cancel", new { });

        var detail = await requester.GetFromJsonAsync<RequestDto>($"/api/v1/requests/{request.Id}");
        Assert.Equal(RequestStatuses.Cancelled, detail!.Status);
    }

    [Fact]
    public async Task Expired_requests_reject_new_responses()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36);

        await using var conn = await OpenDbAsync();
        await conn.ExecuteAsync(
            "update donation_requests set expires_at_utc = now() - interval '1 minute' where id = @id",
            new { id = request.Id });

        var response = await PostAsync(donor, $"/api/v1/requests/{request.Id}/responses",
            new RespondToRequestRequest(ResponseTypes.Accepted));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Expiry_service_expires_requests_and_cancels_pending_runs()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);

        await using var conn = await OpenDbAsync();
        await conn.ExecuteAsync(
            "update donation_requests set expires_at_utc = now() - interval '1 minute' where id = @id",
            new { id = request.Id });

        var expired = await Service<Application.Features.Requests.ExpiryService>()
            .ExpireDueRequestsAsync(correlationId: "test", CancellationToken.None);

        Assert.Equal(1, expired);
        var status = await conn.ExecuteScalarAsync<string>(
            "select status from donation_requests where id = @id", new { id = request.Id });
        Assert.Equal("expired", status);

        var runStatus = await conn.ExecuteScalarAsync<string>(
            "select status from matching_runs where request_id = @id", new { id = request.Id });
        Assert.Equal("cancelled", runStatus);
    }

    [Fact]
    public async Task Nearby_browse_filters_compatibility_radius_and_own_requests()
    {
        // Donor O+ at (31.52, 74.35) with a 25 km radius: compatible with
        // O+, A+, B+, AB+ recipients; NOT with O-, A-, B-, AB-.
        var (donor, _) = await RegisterDonorAsync(31.52, 74.35, "O+");
        var (requester, _) = await RegisterUserAsync();

        var closeCompatible = await CreateRequestAsync(requester, lat: 31.56, lon: 74.36, bloodGroup: "O+");   // ~5 km
        var closerCompatible = await CreateRequestAsync(requester, lat: 31.53, lon: 74.35, bloodGroup: "A+");  // ~1.1 km
        _ = await CreateRequestAsync(requester, lat: 31.60, lon: 74.38, bloodGroup: "B-");                     // incompatible
        _ = await CreateRequestAsync(requester, lat: 31.90, lon: 74.60, bloodGroup: "O+");                     // ~52 km — outside radius
        var own = await CreateRequestAsync(donor, lat: 31.53, lon: 74.36, bloodGroup: "AB+");                  // own request

        var nearby = await ReadAsync<PagedResult<NearbyRequestDto>>(
            await donor.GetAsync("/api/v1/requests/nearby"));

        var ids = nearby.Items.Select(r => r.RequestId).ToList();
        Assert.Contains(closeCompatible.Id, ids);
        Assert.Contains(closerCompatible.Id, ids);
        Assert.DoesNotContain(own.Id, ids);

        // Distance ordering: nearest first; distances are whole-km rounded.
        Assert.True(nearby.Items[0].DistanceKm <= nearby.Items[^1].DistanceKm);
        Assert.All(nearby.Items, r => Assert.True(r.DistanceKm <= 25));

        // Privacy: the browse view carries only the destination's generalized point.
        Assert.All(nearby.Items, r =>
        {
            Assert.InRange(r.ApproxLatitude, 31.0, 32.0);
            Assert.InRange(r.ApproxLongitude, 74.0, 75.0);
        });
    }

    [Fact]
    public async Task Sixth_open_request_is_rejected()
    {
        var (requester, _) = await RegisterUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await CreateRequestAsync(requester, label: $"Request {i}");
        }

        var response = await PostAsync(requester, "/api/v1/requests",
            new CreateRequestRequest("O+", 31.52, 74.35, "One too many", null, null, "routine", null));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
