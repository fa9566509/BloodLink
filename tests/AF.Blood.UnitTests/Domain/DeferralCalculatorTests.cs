using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class DeferralCalculatorTests
{
    private static readonly DeferralPolicy Policy = new(
        Guid.NewGuid(), "TEST-POLICY", DeferralDays: 90, ProvisionalDays: 7, EffectiveFrom: new DateOnly(2026, 1, 1));

    private static readonly DateOnly DonationDay = new(2026, 3, 10);

    [Fact]
    public void Verified_donation_gets_the_full_deferral_from_end_of_donation_day()
    {
        var eligibleAgain = DeferralCalculator.EligibleAgainVerified(Policy, DonationDay);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 23, 59, 59, TimeSpan.Zero).AddDays(90), eligibleAgain);
    }

    [Fact]
    public void Unverified_report_gets_only_the_provisional_restriction()
    {
        var eligibleAgain = DeferralCalculator.ProvisionalEligibleAgain(Policy, DonationDay);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 23, 59, 59, TimeSpan.Zero).AddDays(7), eligibleAgain);
    }

    [Fact]
    public void Zero_provisional_days_means_no_restriction_for_unverified_reports()
    {
        var policy = Policy with { ProvisionalDays = 0 };
        Assert.Null(DeferralCalculator.ProvisionalEligibleAgain(policy, DonationDay));
    }

    [Fact]
    public void Status_based_eligibility_matches_provenance()
    {
        Assert.Equal(
            DeferralCalculator.EligibleAgainVerified(Policy, DonationDay),
            DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.Verified, DonationDay));

        Assert.Equal(
            DeferralCalculator.ProvisionalEligibleAgain(Policy, DonationDay),
            DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.Reported, DonationDay));

        Assert.Equal(
            DeferralCalculator.ProvisionalEligibleAgain(Policy, DonationDay),
            DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.CompletedUnverified, DonationDay));
    }

    [Fact]
    public void Disputed_and_rejected_records_produce_no_deferral()
    {
        Assert.Null(DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.Disputed, DonationDay));
        Assert.Null(DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.Rejected, DonationDay));
    }

    [Fact]
    public void Corrected_records_are_treated_conservatively_as_full_donations()
    {
        Assert.Equal(
            DeferralCalculator.EligibleAgainVerified(Policy, DonationDay),
            DeferralCalculator.EligibleAgain(Policy, DonationStateMachine.Corrected, DonationDay));
    }

    [Fact]
    public void Acceptance_state_produces_no_deferral_at_all()
    {
        // Acceptance never starts a cooldown (spec §11).
        Assert.Null(DeferralCalculator.EligibleAgain(Policy, "accepted_response", DonationDay));
    }
}
