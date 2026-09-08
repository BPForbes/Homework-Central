using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Hosting;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Runs migrate/auth seed after Kestrel is listening, marks /healthz ready, then
/// finishes ticket/neural catalog seed. BackgroundService.StartAsync returns once
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
        bool pauseNeuralEnvironments = DevNeuralEnvironmentPause.ShouldPause(configuration, environment);

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

            if (pauseNeuralEnvironments)
                await PauseActiveNeuralSessionsAsync(stoppingToken);

            readiness.MarkReady();
            logger.LogInformation("Application startup warmup finished; API is ready.");

            await OperationalExceptionGuard.RunAsync(
                () => ApplicationStartupWarmup.RunDeferredCatalogSeedAsync(
                    services,
                    skipDevStartupWarmup,
                    devBypassEnabled,
                    stoppingToken),
                ex =>
                {
                    logger.LogWarning(
                        ex,
                        "Deferred ticket/neural catalog seed failed after /healthz was ready; catalogs retry on next start.");
                    return Task.CompletedTask;
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down during warmup.
        }
    }

    private async Task PauseActiveNeuralSessionsAsync(CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        INeuralNetTrainingService training = scope.ServiceProvider.GetRequiredService<INeuralNetTrainingService>();
        int paused = await training.PauseAllActiveTrainingSessionsAsync(ct);
        logger.LogInformation(
            "Paused {Count} neural training sessions ({Flag}=1).",
            paused,
            DevNeuralEnvironmentPause.EnvVarName);
    }
}
