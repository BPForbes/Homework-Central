using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed class OrphanAttachmentCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<UploadOptions> options,
    ILogger<OrphanAttachmentCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Orphan attachment cleanup failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
