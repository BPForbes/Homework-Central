using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Hosting;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Runs migrate/auth seed after Kestrel is listening. Development marks /healthz ready
/// before ticket/neural catalogs; Production finishes those catalogs first (K8s probe).
/// BackgroundService.StartAsync returns once ExecuteAsync hits its first await.
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
        // Local BackendGate can bind after auth seed. Production /healthz is the K8s readiness
        // probe, so ticket/neural catalogs must finish before Ready there.
        bool deferCatalogsUntilReady = environment.IsDevelopment();

        try
        {
            // Non-transient operational failures mark /healthz failed and stop the host.
            // Development retries unreachable Postgres until this token cancels, so a
            // refused 127.0.0.1 connection does not take the API down.
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
            {
                await OperationalExceptionGuard.RunAsync(
                    () => PauseActiveNeuralSessionsAsync(stoppingToken),
                    ex =>
                    {
                        logger.LogWarning(
                            ex,
                            "Stripped neural pause failed after auth seed; /healthz will still become ready.");
                        return Task.CompletedTask;
                    });
            }

            if (!deferCatalogsUntilReady)
            {
                await OperationalExceptionGuard.RunAsync(
                    () => ApplicationStartupWarmup.RunDeferredCatalogSeedAsync(
                        services,
                        skipDevStartupWarmup,
                        devBypassEnabled,
                        stoppingToken),
                    ex =>
                    {
                        readiness.MarkFailed(ex.Message);
                        logger.LogCritical(ex, "Catalog seed failed; stopping the host.");
                        lifetime.StopApplication();
                        return Task.CompletedTask;
                    });
                if (readiness.State == ApplicationReadyState.Failed)
                    return;
            }

            readiness.MarkReady();
            logger.LogInformation("Application startup warmup finished; API is ready.");

            if (!deferCatalogsUntilReady)
                return;

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
