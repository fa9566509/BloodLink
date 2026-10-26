using AF.Blood.Application.Features.Matching;
using Microsoft.Extensions.Options;

namespace AF.Blood.Worker.Jobs;

/// <summary>Claims and processes due matching runs on a timer. Bounded work per
/// cycle, cancellation-aware, safe to restart (ADR-0006).</summary>
public sealed class MatchingRunJob(
    MatchingService matching,
    IOptions<Application.Options.WorkerOptions> options,
    ILogger<MatchingRunJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(2, options.Value.PollSeconds));
        using var timer = new PeriodicTimer(pollInterval);

        logger.LogInformation("MatchingRunJob started (poll every {Seconds}s).", pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var processed = await matching.ProcessDueRunsAsync(
                    options.Value.InstanceName, options.Value.MaxRunsPerCycle, stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Processed {Count} matching run(s).", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MatchingRunJob cycle failed; retrying next tick.");
            }
        }

        logger.LogInformation("MatchingRunJob stopping.");
    }
}
