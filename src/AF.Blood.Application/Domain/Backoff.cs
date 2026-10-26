namespace AF.Blood.Application.Domain;

/// <summary>Bounded exponential backoff with jitter for outbox retries.</summary>
public static class Backoff
{
    /// <summary>Delay before attempt number <paramref name="attempt"/> (1-based):
    /// base * 2^(attempt-1), capped at maxSeconds, plus deterministic-free jitter
    /// of ±20% supplied by the caller-provided random.</summary>
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, Random random)
    {
        var exponent = Math.Min(attempt - 1, 16);
        var seconds = baseDelay.TotalSeconds * Math.Pow(2, exponent);
        var capped = TimeSpan.FromSeconds(Math.Min(seconds, maxDelay.TotalSeconds));
        var jitterFactor = 0.8 + (random.NextDouble() * 0.4); // 0.8 .. 1.2
        return TimeSpan.FromSeconds(capped.TotalSeconds * jitterFactor);
    }
}
