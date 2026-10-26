using AF.Blood.Application.Features.Notifications;
using Microsoft.Extensions.Options;

namespace AF.Blood.Worker.Jobs;

/// <summary>Delivers queued notifications through the push dispatcher on a timer.
/// All delivery logic lives in <see cref="OutboxDeliveryService"/> (Application)
/// so it is testable without a host.</summary>
public sealed class OutboxDeliveryJob(
    OutboxDeliveryService delivery,
    IOptions<Application.Options.WorkerOptions> workerOptions,
    ILogger<OutboxDeliveryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(2, workerOptions.Value.PollSeconds / 2.0));
        using var timer = new PeriodicTimer(pollInterval);

        logger.LogInformation("OutboxDeliveryJob started (poll every {Seconds}s).", pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var recovered = await delivery.RecoverExpiredLeasesAsync(stoppingToken);
                if (recovered > 0)
                {
                    logger.LogWarning("Recovered {Count} abandoned outbox lease(s).", recovered);
                }

                var delivered = await delivery.DeliverBatchAsync(stoppingToken);
                if (delivered > 0)
                {
                    logger.LogInformation("Delivered {Count} notification(s).", delivered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxDeliveryJob cycle failed; retrying next tick.");
            }
        }

        logger.LogInformation("OutboxDeliveryJob stopping.");
    }
}
