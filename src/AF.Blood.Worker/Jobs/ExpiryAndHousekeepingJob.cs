using AF.Blood.Application.Features.Requests;
using Microsoft.Extensions.Options;

namespace AF.Blood.Worker.Jobs;

/// <summary>Expires open requests past their deadline and runs database
/// housekeeping (stale codes/tokens). Eligibility never depends on this job
/// being on time — it reads persisted state (spec §17).</summary>
public sealed class ExpiryAndHousekeepingJob(
    ExpiryService expiry,
    IOptions<Application.Options.WorkerOptions> options,
    ILogger<ExpiryAndHousekeepingJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, options.Value.PollSeconds * 3)));

        logger.LogInformation("ExpiryAndHousekeepingJob started.");

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var expired = await expiry.ExpireDueRequestsAsync(correlationId: null, stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Expired {Count} request(s).", expired);
                }

                await expiry.HousekeepingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ExpiryAndHousekeepingJob cycle failed; retrying next tick.");
            }
        }

        logger.LogInformation("ExpiryAndHousekeepingJob stopping.");
    }
}
