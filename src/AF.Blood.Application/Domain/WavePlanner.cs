namespace AF.Blood.Application.Domain;

public record WaveDecision(bool ScheduleNext, int NextWaveIndex, int NextRadiusKm, TimeSpan Delay);

/// <summary>
/// Wave progression rules for donor notification (operational defaults, not
/// medical or geographic rules):
///   1. Waves walk the configured radius ladder (e.g. 25 → 50 → 100 km).
///   2. A wave that notifies at least <see cref="minCandidatesToStop"/> new
///      donors is enough — no further wave is scheduled.
///   3. If the request is closed (matched/fulfilled/cancelled/expired) no
///      further wave runs.
///   4. When the largest radius is reached with too few candidates, waves stop.
/// </summary>
public static class WavePlanner
{
    public static WaveDecision DecideNext(
        int completedWaveIndex,
        int radiusUsedKm,
        int candidatesNotifiedThisWave,
        IReadOnlyList<int> radiiKm,
        int minCandidatesToStop,
        TimeSpan waveDelay)
    {
        if (candidatesNotifiedThisWave >= minCandidatesToStop)
        {
            return new WaveDecision(false, 0, 0, TimeSpan.Zero);
        }

        var nextRadius = radiiKm.FirstOrDefault(r => r > radiusUsedKm);
        if (nextRadius == 0)
        {
            return new WaveDecision(false, 0, 0, TimeSpan.Zero);
        }

        return new WaveDecision(
            ScheduleNext: true,
            NextWaveIndex: completedWaveIndex + 1,
            NextRadiusKm: nextRadius,
            Delay: waveDelay);
    }
}
