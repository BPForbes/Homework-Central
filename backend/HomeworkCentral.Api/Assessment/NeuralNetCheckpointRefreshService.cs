using HomeworkCentral.Api.Services;

namespace HomeworkCentral.Api.Assessment;

/// <summary>Reloads each isolated hashed-MLP chat monitor when another worker publishes its canonical generation.</summary>
public sealed class NeuralNetCheckpointRefreshService(
    IServiceScopeFactory scopes,
    IChatMonitoringNeuralModelFactory chatMonitoringModels,
    IApplicationReadiness readiness,
    INeuralNetTrainingProgressStore progressStore,
    ILogger<NeuralNetCheckpointRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(180);

    private readonly Dictionary<NeuralModelKindChatMonitoring, string> loadedChecksums = [];

    private static bool IsTimeout(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
                return true;
            if (current.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await readiness.WaitUntilReadyAsync(stoppingToken))
            return;

        TimeSpan delay = BaseDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (progressStore.HasActiveTraining())
                {
                    await Task.Delay(BaseDelay, stoppingToken);
                    continue;
                }

                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                NeuralNetCheckpointStore store = scope.ServiceProvider.GetRequiredService<NeuralNetCheckpointStore>();
                foreach (NeuralModelKindChatMonitoring chatMonitoringKind in Enum.GetValues<NeuralModelKindChatMonitoring>())
                {
                    HomeworkCentral.Api.Models.NeuralNetCanonicalCheckpoint? checkpoint = await store.GetCurrentAsync(chatMonitoringKind, stoppingToken);
                    IChatMonitoringNeuralModel model = chatMonitoringModels.Get(chatMonitoringKind);
                    if (checkpoint is not null && !string.Equals(checkpoint.Checksum, loadedChecksums.GetValueOrDefault(chatMonitoringKind), StringComparison.Ordinal) && model is IChatMonitoringNeuralModelTelemetry telemetry)
                    {
                        int parameterCount = telemetry.GetTopologySnapshot().Parameters.Count;
                        telemetry.LoadParameterSnapshot(new(checkpoint.Generation, 0, "ieee754-float32-le", "dense-base64", parameterCount, checkpoint.ParametersBase64, checkpoint.Checksum));
                        loadedChecksums[chatMonitoringKind] = checkpoint.Checksum;
                        logger.LogInformation("Loaded {ChatMonitoringKind} canonical neural checkpoint {Generation}.", chatMonitoringKind, checkpoint.Generation);
                    }
                }

                delay = BaseDelay;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown cancels Delay; do not surface TaskCanceledException (StopHost behavior).
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Canonical neural checkpoint refresh failed.");
                if (IsTimeout(ex))
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxBackoff.TotalSeconds));
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
