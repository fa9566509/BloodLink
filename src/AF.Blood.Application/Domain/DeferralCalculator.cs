namespace AF.Blood.Application.Domain;

/// <summary>Snapshot of an approved deferral policy version.</summary>
public record DeferralPolicy(
    Guid Id,
    string Version,
    int DeferralDays,
    int ProvisionalDays,
    DateOnly EffectiveFrom);

/// <summary>
/// Computes eligibility dates from an approved policy version. It never invents
/// intervals: everything comes from the persisted policy. Expiry of a deferral
/// countdown is NOT medical clearance — UI copy must always say that final
/// eligibility is determined by the donation centre.
/// </summary>
public static class DeferralCalculator
{
    /// <summary>Eligible-again instant for a verified donation under the policy.</summary>
    public static DateTimeOffset EligibleAgainVerified(DeferralPolicy policy, DateOnly donationDate) =>
        ToUtcEndOfDay(donationDate).AddDays(policy.DeferralDays);

    /// <summary>Provisional restriction for a self-reported, not-yet-verified
    /// donation. Returns null when the policy sets no provisional restriction —
    /// an explicit, configurable policy decision.</summary>
    public static DateTimeOffset? ProvisionalEligibleAgain(DeferralPolicy policy, DateOnly donationDate) =>
        policy.ProvisionalDays <= 0
            ? null
            : ToUtcEndOfDay(donationDate).AddDays(policy.ProvisionalDays);

    /// <summary>Combined deferral for a donation record in its current state:
    /// verified (or administratively corrected, treated conservatively as a full
    /// donation) → full deferral; unverified-but-counted → provisional only.</summary>
    public static DateTimeOffset? EligibleAgain(DeferralPolicy policy, string donationStatus, DateOnly donationDate) =>
        donationStatus is DonationStateMachine.Verified or DonationStateMachine.Corrected
            ? EligibleAgainVerified(policy, donationDate)
            : DonationStateMachine.CountsAsDonation(donationStatus)
                ? ProvisionalEligibleAgain(policy, donationDate)
                : null;

    private static DateTimeOffset ToUtcEndOfDay(DateOnly date) =>
        new DateTimeOffset(date, new TimeOnly(23, 59, 59), TimeSpan.Zero);
}
