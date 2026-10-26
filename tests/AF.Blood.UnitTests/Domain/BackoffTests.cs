using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class BackoffTests
{
    [Fact]
    public void Delay_grows_exponentially_and_is_capped()
    {
        var random = new Random(42);
        var baseDelay = TimeSpan.FromSeconds(5);
        var maxDelay = TimeSpan.FromSeconds(900);

        var d1 = Backoff.Delay(1, baseDelay, maxDelay, random);
        var d2 = Backoff.Delay(3, baseDelay, maxDelay, random);
        var d99 = Backoff.Delay(99, baseDelay, maxDelay, random);

        Assert.True(d2 > d1, "later attempts back off longer");
        Assert.True(d99 <= maxDelay, "delay is capped");
    }

    [Fact]
    public void Jitter_stays_within_twenty_percent()
    {
        var random = new Random(7);
        for (var i = 1; i <= 20; i++)
        {
            var delay = Backoff.Delay(i, TimeSpan.FromSeconds(10), TimeSpan.FromHours(1), random);
            var uncapped = TimeSpan.FromSeconds(10 * Math.Pow(2, Math.Min(i - 1, 16)));
            var expected = uncapped > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : uncapped;
            Assert.InRange(delay.TotalSeconds, expected.TotalSeconds * 0.8, expected.TotalSeconds * 1.2);
        }
    }
}
