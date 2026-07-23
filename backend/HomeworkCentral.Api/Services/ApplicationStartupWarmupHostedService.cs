using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Hosting;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Runs migrate/seed after Kestrel is listening. BackgroundService.StartAsync returns once
/// ExecuteAsync hits its first await, so /healthz is reachable during warmup.
/// </summary>
public sealed class ApplicationStartupWarmupHostedService(
    IServiceProvider services,
    IApplicationReadiness readiness,
    IHostApplicationLifetime lifetime,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<ApplicationStartupWarmupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so host start (and Kestrel listen) is not blocked on migrate/seed.
        await Task.Yield();

        bool skipDevStartupWarmup = DevStartupWarmup.ShouldSkip(configuration, environment);
        bool devBypassEnabled = DevBypass.IsEnabled(configuration, environment);
        bool eagerPersonaProvisioning = DevPersonaEagerProvisioning.IsEnabled(configuration);

        try
        {
            // Operational failures mark /healthz failed and stop the host; unexpected bugs still bubble.
            await OperationalExceptionGuard.RunAsync(
                () => ApplicationStartupWarmup.RunAsync(
                    services,
                    environment.IsDevelopment(),
                    skipDevStartupWarmup,
                    devBypassEnabled,
                    eagerPersonaProvisioning,
                    stoppingToken),
                ex =>
                {
                    readiness.MarkFailed(ex.Message);
                    logger.LogCritical(ex, "Application startup warmup failed; stopping the host.");
                    lifetime.StopApplication();
                    return Task.CompletedTask;
                });
            if (readiness.State == ApplicationReadyState.Failed)
                return;

            readiness.MarkReady();
            logger.LogInformation("Application startup warmup finished; API is ready.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down during warmup.
        }
    }
}
