using HomeworkCentral.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed class OrphanAttachmentCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<UploadOptions> options,
    IApplicationReadiness readiness,
    ILogger<OrphanAttachmentCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await readiness.WaitUntilReadyAsync(stoppingToken))
            return;

        UploadOptions opts = options.Value;
        TimeSpan interval = TimeSpan.FromMinutes(opts.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IOrphanAttachmentCleanupService cleanup =
                    scope.ServiceProvider.GetRequiredService<IOrphanAttachmentCleanupService>();
                int removed = await cleanup.PurgeOrphansAsync(stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Purged {Count} orphan attachments", removed);

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown cancels Delay; do not surface TaskCanceledException (StopHost behavior).
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Orphan attachment cleanup failed");
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
