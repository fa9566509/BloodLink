using AF.Blood.Application.Domain;
using Xunit;

namespace AF.Blood.UnitTests.Domain;

public class WavePlannerTests
{
    private static readonly int[] Radii = [25, 50, 100];
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(20);

    [Fact]
    public void Enough_candidates_stops_the_ladder()
    {
        var decision = WavePlanner.DecideNext(0, 25, candidatesNotifiedThisWave: 10, Radii, minCandidatesToStop: 10, Delay);
        Assert.False(decision.ScheduleNext);
    }

    [Fact]
    public void Few_candidates_expand_to_the_next_radius()
    {
        var decision = WavePlanner.DecideNext(0, 25, candidatesNotifiedThisWave: 3, Radii, minCandidatesToStop: 10, Delay);
        Assert.True(decision.ScheduleNext);
        Assert.Equal(1, decision.NextWaveIndex);
        Assert.Equal(50, decision.NextRadiusKm);
        Assert.Equal(Delay, decision.Delay);
    }

    [Fact]
    public void Reaching_the_largest_radius_stops_the_ladder()
    {
        var decision = WavePlanner.DecideNext(1, 100, candidatesNotifiedThisWave: 0, Radii, minCandidatesToStop: 10, Delay);
        Assert.False(decision.ScheduleNext);
    }

    [Fact]
    public void Zero_candidates_expand_radius_and_increase_wave_index()
    {
        var decision = WavePlanner.DecideNext(2, 50, 0, Radii, 10, Delay);
        Assert.True(decision.ScheduleNext);
        Assert.Equal(3, decision.NextWaveIndex);
        Assert.Equal(100, decision.NextRadiusKm);
    }
}
