using AF.Blood.IntegrationTests.Support;
using Dapper;
using Xunit;
using MatchingService = AF.Blood.Application.Features.Matching.MatchingService;

namespace AF.Blood.IntegrationTests;

public class MatchingWaveTests : IntegrationTestBase
{
    private async Task<MatchingService> Matching() =>
        Service<Application.Features.Matching.MatchingService>();

    [Fact]
    public async Task Wave_zero_notifies_only_eligible_candidates()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");

        var (compatibleNear1Client, compatibleNear1Auth) = await RegisterDonorAsync(31.53, 74.36, "O-");
        var (compatibleNear2Client, compatibleNear2Auth) = await RegisterDonorAsync(31.52, 74.34, "O+"); // same-group donor
        _ = await RegisterDonorAsync(31.53, 74.35, "B+");                       // incompatible group
        _ = await RegisterDonorAsync(31.99, 74.99, "O-");                       // ~72 km — outside radius
        _ = await RegisterDonorAsync(31.53, 74.35, "O-", available: false);     // unavailable
        var (withdrawn, _) = await RegisterDonorAsync(31.53, 74.35, "O-");      // withdraws consent below
        var (responder, responderAuth) = await RegisterDonorAsync(31.53, 74.35, "O-"); // declines below

        await PutAsync(withdrawn, "/api/v1/donors/me/consent", new AF.Blood.Contracts.DonorConsentRequest(false, "v", null, null));
        var decline = await PostAsync(responder, $"/api/v1/requests/{request.Id}/responses",
            new AF.Blood.Contracts.RespondToRequestRequest("declined"));
        Assert.True(decline.IsSuccessStatusCode);

        await RunMatchingAsync();

        await using var conn = await OpenDbAsync();
        var notified = (await conn.QueryAsync<Guid>(
            "select donor_user_id from matching_run_donors where request_id = @id",
            new { id = request.Id })).ToHashSet();

        Assert.Contains(compatibleNear1Auth.User.Id, notified);
        Assert.Contains(compatibleNear2Auth.User.Id, notified);
        Assert.Equal(2, notified.Count);

        // Notification history + outbox queued for exactly those donors.
        var history = await conn.ExecuteScalarAsync<int>(
            "select count(*) from notification_history where request_id = @id", new { id = request.Id });
        Assert.Equal(2, history);
        var outbox = await conn.ExecuteScalarAsync<int>(
            "select count(*) from notification_outbox", new { });
        Assert.Equal(2, outbox);
    }

    [Fact]
    public async Task Enough_candidates_in_wave_zero_stop_the_wave_ladder()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");

        for (var i = 0; i < 10; i++)
        {
            await RegisterDonorAsync(31.52 + i * 0.005, 74.35 + i * 0.005, "O-");
        }

        await RunMatchingAsync();

        await using var conn = await OpenDbAsync();
        var runs = await conn.ExecuteScalarAsync<int>("select count(*) from matching_runs where request_id = @id",
            new { id = request.Id });
        Assert.Equal(1, runs); // no wave 1 scheduled
    }

    [Fact]
    public async Task Few_candidates_expand_radius_on_the_next_wave()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (farDonorClient, farDonorAuth) = await RegisterDonorAsync(31.86, 74.60, "O-"); // ~40 km

        await RunMatchingAsync();

        await using var conn = await OpenDbAsync();
        var wave0Notified = await conn.ExecuteScalarAsync<int>(
            "select count(*) from matching_run_donors m join matching_runs r on r.id = m.matching_run_id " +
            "where r.request_id = @id and r.wave_index = 0", new { id = request.Id });
        Assert.Equal(0, wave0Notified); // outside 25 km

        var wave1 = await conn.QuerySingleAsync<(int Radius, DateTimeOffset Due)>(
            "select radius_km, due_at_utc from matching_runs where request_id = @id and wave_index = 1",
            new { id = request.Id });
        Assert.Equal(50, wave1.Radius);
        Assert.True(wave1.Due > DateTimeOffset.UtcNow);

        // Force the wave due and process it.
        await conn.ExecuteAsync("update matching_runs set due_at_utc = now() - interval '1 second' " +
                                "where request_id = @id and wave_index = 1", new { id = request.Id });
        await RunMatchingAsync();

        var notified = (await conn.QueryAsync<Guid>(
            "select donor_user_id from matching_run_donors where request_id = @id",
            new { id = request.Id })).ToList();
        Assert.Contains(farDonorAuth.User.Id, notified);
    }

    [Fact]
    public async Task Deferred_donors_are_excluded_from_matching()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (deferredDonorClient, deferredDonorAuth) = await RegisterDonorAsync(31.53, 74.36, "O-");

        await using var conn = await OpenDbAsync();
        await conn.ExecuteAsync("""
            insert into donor_deferrals (donor_user_id, deferral_type, eligible_again_at)
            values (@donor, 'verified', now() + interval '30 days')
            """,
            new { donor = deferredDonorAuth.User.Id });

        await RunMatchingAsync();

        var notified = await conn.ExecuteScalarAsync<int>(
            "select count(*) from matching_run_donors where request_id = @id", new { id = request.Id });
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task A_cancelled_request_stops_its_pending_wave()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        _ = await RegisterDonorAsync(31.53, 74.36, "O-");

        await PostAsync(requester, $"/api/v1/requests/{request.Id}/cancel", new { });

        await RunMatchingAsync();

        await using var conn = await OpenDbAsync();
        var notified = await conn.ExecuteScalarAsync<int>(
            "select count(*) from matching_run_donors where request_id = @id", new { id = request.Id });
        Assert.Equal(0, notified);
    }

    [Fact]
    public async Task Lease_recovery_resets_abandoned_runs()
    {
        var (requester, _) = await RegisterUserAsync();
        await CreateRequestAsync(requester);

        await using var conn = await OpenDbAsync();
        await conn.ExecuteAsync("""
            update matching_runs
               set status = 'processing', leased_by = 'dead-worker', leased_until = now() - interval '1 minute'
            """);

        var recovered = await (await Matching()).RecoverExpiredLeasesAsync(CancellationToken.None);
        Assert.Equal(1, recovered);

        var status = await conn.ExecuteScalarAsync<string>("select status from matching_runs");
        Assert.Equal("pending", status);
    }

}
