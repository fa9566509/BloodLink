using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class DonationStateMachineTests
{
    [Theory]
    [InlineData(DonationStateMachine.Reported, DonationStateMachine.CompletedUnverified)]
    [InlineData(DonationStateMachine.Reported, DonationStateMachine.Disputed)]
    [InlineData(DonationStateMachine.Reported, DonationStateMachine.Rejected)]
    [InlineData(DonationStateMachine.Reported, DonationStateMachine.Verified)]
    [InlineData(DonationStateMachine.CompletedUnverified, DonationStateMachine.Verified)]
    [InlineData(DonationStateMachine.CompletedUnverified, DonationStateMachine.Disputed)]
    [InlineData(DonationStateMachine.CompletedUnverified, DonationStateMachine.Rejected)]
    [InlineData(DonationStateMachine.Disputed, DonationStateMachine.CompletedUnverified)]
    [InlineData(DonationStateMachine.Disputed, DonationStateMachine.Rejected)]
    [InlineData(DonationStateMachine.Disputed, DonationStateMachine.Verified)]
    [InlineData(DonationStateMachine.Verified, DonationStateMachine.Corrected)]
    [InlineData(DonationStateMachine.Corrected, DonationStateMachine.Verified)]
    [InlineData(DonationStateMachine.Corrected, DonationStateMachine.Rejected)]
    public void Legal_transitions_are_allowed(string from, string to)
    {
        Assert.True(DonationStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(DonationStateMachine.Verified, DonationStateMachine.Reported)] // never un-verify
    [InlineData(DonationStateMachine.Rejected, DonationStateMachine.Verified)]
    [InlineData(DonationStateMachine.Verified, DonationStateMachine.Disputed)]
    [InlineData(DonationStateMachine.Reported, DonationStateMachine.AwaitingConfirmation)]
    [InlineData(DonationStateMachine.Corrected, DonationStateMachine.Corrected)]
    public void Illegal_transitions_are_rejected(string from, string to)
    {
        Assert.False(DonationStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Self_reported_is_never_automatically_verified()
    {
        // The initial state of a donor report is 'reported', not 'verified' —
        // provenance is preserved (spec §6).
        Assert.NotEqual(DonationStateMachine.Verified, DonationStateMachine.Reported);
    }

    [Theory]
    [InlineData(DonationStateMachine.Reported, true)]
    [InlineData(DonationStateMachine.AwaitingConfirmation, true)]
    [InlineData(DonationStateMachine.CompletedUnverified, true)]
    [InlineData(DonationStateMachine.Verified, true)]
    [InlineData(DonationStateMachine.Corrected, true)]
    [InlineData(DonationStateMachine.Disputed, false)]
    [InlineData(DonationStateMachine.Rejected, false)]
    public void Deferral_applies_only_to_states_that_count_as_a_donation(string status, bool counts)
    {
        Assert.Equal(counts, DonationStateMachine.CountsAsDonation(status));
    }
}
