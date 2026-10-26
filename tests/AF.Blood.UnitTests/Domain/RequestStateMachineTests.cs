using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class RequestStateMachineTests
{
    [Theory]
    [InlineData(RequestStateMachine.Open, RequestStateMachine.Matched)]
    [InlineData(RequestStateMachine.Open, RequestStateMachine.Cancelled)]
    [InlineData(RequestStateMachine.Open, RequestStateMachine.Expired)]
    [InlineData(RequestStateMachine.Matched, RequestStateMachine.Fulfilled)]
    [InlineData(RequestStateMachine.Matched, RequestStateMachine.Cancelled)]
    [InlineData(RequestStateMachine.Matched, RequestStateMachine.UnderReview)]
    [InlineData(RequestStateMachine.Fulfilled, RequestStateMachine.UnderReview)]
    [InlineData(RequestStateMachine.UnderReview, RequestStateMachine.Fulfilled)]
    [InlineData(RequestStateMachine.UnderReview, RequestStateMachine.Cancelled)]
    [InlineData(RequestStateMachine.UnderReview, RequestStateMachine.Open)]
    public void Legal_transitions_are_allowed(string from, string to)
    {
        Assert.True(RequestStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RequestStateMachine.Open, RequestStateMachine.Fulfilled)] // no donation report yet
    [InlineData(RequestStateMachine.Open, RequestStateMachine.UnderReview)]
    [InlineData(RequestStateMachine.Matched, RequestStateMachine.Open)] // donor slot is final
    [InlineData(RequestStateMachine.Matched, RequestStateMachine.Expired)] // matched requests do not auto-expire
    [InlineData(RequestStateMachine.Fulfilled, RequestStateMachine.Matched)]
    [InlineData(RequestStateMachine.Cancelled, RequestStateMachine.Open)]
    [InlineData(RequestStateMachine.Expired, RequestStateMachine.Open)]
    [InlineData(RequestStateMachine.Fulfilled, RequestStateMachine.Fulfilled)]
    public void Illegal_transitions_are_rejected(string from, string to)
    {
        Assert.False(RequestStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void Acceptance_does_not_fulfil_a_request()
    {
        Assert.False(RequestStateMachine.CanTransition(RequestStateMachine.Open, RequestStateMachine.Fulfilled));
    }

    [Theory]
    [InlineData(RequestStateMachine.Cancelled, true)]
    [InlineData(RequestStateMachine.Expired, true)]
    [InlineData(RequestStateMachine.Fulfilled, true)]
    [InlineData(RequestStateMachine.Open, false)]
    [InlineData(RequestStateMachine.Matched, false)]
    public void Closed_states_stop_waves(string status, bool closed)
    {
        Assert.Equal(closed, RequestStateMachine.IsClosed(status));
    }

    [Fact]
    public void Requests_accept_responses_only_while_open_and_unexpired()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(RequestStateMachine.AcceptsResponses(RequestStateMachine.Open, now.AddHours(1), now));
        Assert.False(RequestStateMachine.AcceptsResponses(RequestStateMachine.Open, now, now));
        Assert.False(RequestStateMachine.AcceptsResponses(RequestStateMachine.Open, now.AddHours(-1), now));
        Assert.False(RequestStateMachine.AcceptsResponses(RequestStateMachine.Matched, now.AddHours(1), now));
        Assert.False(RequestStateMachine.AcceptsResponses(RequestStateMachine.Cancelled, now.AddHours(1), now));
    }

    [Fact]
    public void Donation_reports_are_accepted_from_matched_or_fulfilled_requests()
    {
        Assert.True(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.Matched));
        Assert.True(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.Fulfilled));
        Assert.False(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.Open));
        Assert.False(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.Cancelled));
        Assert.False(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.Expired));
        Assert.False(RequestStateMachine.AcceptsDonationReport(RequestStateMachine.UnderReview));
    }
}
