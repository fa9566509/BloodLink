using System.Net;
using System.Net.Http.Json;
using AF.Blood.Contracts;
using AF.Blood.IntegrationTests.Support;
using Dapper;
using Xunit;

namespace AF.Blood.IntegrationTests;

public class DonationFlowTests : IntegrationTestBase
{
    [Fact]
    public async Task Report_acknowledge_verify_lifecycle_applies_policy_backed_deferrals()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        // Donor reports a donation today.
        var report = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), "Donated at General Hospital"));
        Assert.True(report.IsSuccessStatusCode, await report.Content.ReadAsStringAsync());
        var record = await ReadAsync<DonationRecordDto>(report);

        Assert.Equal(DonationStatuses.Reported, record.Status);
        Assert.Equal("DEV-SEED-0.1-whole-blood", record.PolicyVersion); // dev seed policy, never a medical claim

        // Provisional restriction is active while the report is unconfirmed.
        var provisional = await donor.GetFromJsonAsync<DonorEligibilityDto>("/api/v1/donors/me/eligibility");
        Assert.True(provisional!.IsDeferred);
        Assert.Equal(DeferralTypes.Provisional, provisional.DeferralType);

        // Requester acknowledges -> completed but unverified (provenance preserved).
        var ack = await PostAsync(requester, $"/api/v1/requests/{request.Id}/donation-acknowledgement",
            new AcknowledgementRequest(true, "Donation happened at the hospital."));
        var acknowledged = await ReadAsync<DonationRecordDto>(ack);
        Assert.Equal(DonationStatuses.CompletedUnverified, acknowledged.Status);

        // Admin verifies -> full deferral replaces the provisional one.
        var (admin, _) = await RegisterAdminAsync();
        var verify = await PostAsync(admin, $"/api/v1/admin/donations/{record.Id}/verify",
            new AdminDonationDecisionRequest("Centre record matched.", null));
        var verified = await ReadAsync<DonationRecordDto>(verify);
        Assert.Equal(DonationStatuses.Verified, verified.Status);
        Assert.NotNull(verified.EligibleAgainAtUtc);
        Assert.InRange((verified.EligibleAgainAtUtc!.Value - DateTimeOffset.UtcNow).TotalDays, 89, 91);

        // The deferral row is now 'verified', the provisional row revoked.
        await using var conn = await OpenDbAsync();
        var deferrals = (await conn.QueryAsync<(string Type, bool Revoked)>(
            "select deferral_type, revoked_at_utc is not null as revoked from donor_deferrals where donation_record_id = (select id from donation_records where id = @id)",
            new { id = record.Id })).ToList();
        Assert.Contains(("provisional", true), deferrals);
        Assert.Contains(("verified", false), deferrals);

        // Request is fulfilled — reporting, not acceptance, completed it.
        var detail = await requester.GetFromJsonAsync<RequestDto>($"/api/v1/requests/{request.Id}");
        Assert.Equal(RequestStatuses.Fulfilled, detail!.Status);

        // Eligibility message keeps the safety boundary even after deferral.
        var afterVerify = await donor.GetFromJsonAsync<DonorEligibilityDto>("/api/v1/donors/me/eligibility");
        Assert.True(afterVerify!.IsDeferred);
        Assert.InRange(afterVerify.DeferralRemainingDays!.Value, 89, 91);
    }

    [Fact]
    public async Task Dispute_routes_to_admin_review_and_rejection_removes_the_restriction()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester, bloodGroup: "O+");
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var report = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        var record = await ReadAsync<DonationRecordDto>(report);

        // The administrator must exist before the dispute is raised — dispute
        // notifications go to the administrators present at that moment.
        var (admin, _) = await RegisterAdminAsync();

        var dispute = await PostAsync(requester, $"/api/v1/requests/{request.Id}/donation-acknowledgement",
            new AcknowledgementRequest(false, "This never happened."));
        var disputed = await ReadAsync<DonationRecordDto>(dispute);
        Assert.Equal(DonationStatuses.Disputed, disputed.Status);

        var detail = await requester.GetFromJsonAsync<RequestDto>($"/api/v1/requests/{request.Id}");
        Assert.Equal(RequestStatuses.UnderReview, detail!.Status);

        // Admin rejects: restriction removed, request closed as cancelled.
        var reject = await PostAsync(admin, $"/api/v1/admin/donations/{record.Id}/reject",
            new AdminDonationDecisionRequest("No supporting evidence.", null));
        var rejected = await ReadAsync<DonationRecordDto>(reject);
        Assert.Equal(DonationStatuses.Rejected, rejected.Status);

        var eligibility = await donor.GetFromJsonAsync<DonorEligibilityDto>("/api/v1/donors/me/eligibility");
        Assert.False(eligibility!.IsDeferred);

        var afterReject = await requester.GetFromJsonAsync<RequestDto>($"/api/v1/requests/{request.Id}");
        Assert.Equal(RequestStatuses.Cancelled, afterReject!.Status);

        // Admins were notified for review via the outbox.
        await using var conn = await OpenDbAsync();
        var adminNotified = await conn.ExecuteScalarAsync<int>("""
            select count(*) from notification_history nh
            join "AspNetUserRoles" ur on ur."UserId" = nh.user_id
            join "AspNetRoles" r on r."Id" = ur."RoleId"
            where r."NormalizedName" = 'ADMINISTRATOR' and nh.type = 'donation_disputed'
            """);
        Assert.True(adminNotified >= 1);
    }

    [Fact]
    public async Task Only_the_accepting_donor_can_report()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var (outsider, _) = await RegisterDonorAsync(31.60, 74.40, "O-");
        var response = await PostAsync(outsider, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Second_report_for_the_same_request_conflicts()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var first = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.True(first.IsSuccessStatusCode);

        var second = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Future_donation_dates_are_rejected()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var response = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task New_policy_versions_apply_to_new_reports_only()
    {
        var (admin, _) = await RegisterAdminAsync();

        // Existing record under the dev seed policy: its provisional deferral
        // (7 days) is active, which also blocks this donor from accepting again.
        var (requester, _) = await RegisterUserAsync();
        var request1 = await CreateRequestAsync(requester);
        var (donor1, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor1, request1.Id);
        var report1 = await PostAsync(donor1, $"/api/v1/requests/{request1.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        var record1 = await ReadAsync<DonationRecordDto>(report1);

        // Create + activate policy v2 with no provisional restriction.
        var create = await PostAsync(admin, "/api/v1/admin/policies",
            new CreatePolicyVersionRequest("TEST-POLICY-v2", 120, 0, DateOnly.FromDateTime(DateTime.UtcNow),
                "QA Approver", "TEST-REF-2", "Test policy"));
        create.EnsureSuccessStatusCode();
        var v2 = await ReadAsync<PolicyVersionDto>(create);
        var activate = await PostAsync(admin, $"/api/v1/admin/policies/{v2.Id}/activate", new { });
        activate.EnsureSuccessStatusCode();

        // A fresh donor reports under v2: no provisional restriction applies.
        var request2 = await CreateRequestAsync(requester);
        var (donor2, _) = await RegisterDonorAsync(31.54, 74.37, "O-");
        await AcceptAsync(donor2, request2.Id);
        var report2 = await PostAsync(donor2, $"/api/v1/requests/{request2.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow), null));
        var record2 = await ReadAsync<DonationRecordDto>(report2);

        Assert.Equal("DEV-SEED-0.1-whole-blood", record1.PolicyVersion); // old record keeps its policy
        Assert.Equal("TEST-POLICY-v2", record2.PolicyVersion);
        Assert.Null(record2.EligibleAgainAtUtc); // provisional_days = 0 → no restriction

        await using var conn = await OpenDbAsync();
        var deferralsForRecord2 = await conn.ExecuteScalarAsync<int>(
            "select count(*) from donor_deferrals where donation_record_id = @id", new { id = record2.Id });
        Assert.Equal(0, deferralsForRecord2);

        var eligibility = await donor2.GetFromJsonAsync<DonorEligibilityDto>("/api/v1/donors/me/eligibility");
        Assert.False(eligibility!.IsDeferred);
    }

    [Fact]
    public async Task Administrative_correction_recomputes_the_deferral()
    {
        var (requester, _) = await RegisterUserAsync();
        var request = await CreateRequestAsync(requester);
        var (donor, _) = await RegisterDonorAsync(31.53, 74.36, "O-");
        await AcceptAsync(donor, request.Id);

        var report = await PostAsync(donor, $"/api/v1/requests/{request.Id}/donation-report",
            new DonationReportRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10), null));
        var record = await ReadAsync<DonationRecordDto>(report);

        var (admin, _) = await RegisterAdminAsync();
        var correct = await PostAsync(admin, $"/api/v1/admin/donations/{record.Id}/correct",
            new AdminDonationDecisionRequest("Date entered wrong by donor.", DateOnly.FromDateTime(DateTime.UtcNow)));
        var corrected = await ReadAsync<DonationRecordDto>(correct);

        Assert.Equal(DonationStatuses.Corrected, corrected.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), corrected.DonationDate);
        Assert.NotNull(corrected.EligibleAgainAtUtc);

        // Append-only history records the correction.
        await using var conn = await OpenDbAsync();
        var events = (await conn.QueryAsync<string>(
            "select event_type from donation_record_events where donation_record_id = @id order by created_at_utc",
            new { id = record.Id })).ToList();
        Assert.Contains("reported", events);
        Assert.Contains("corrected", events);
    }
}
