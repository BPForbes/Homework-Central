namespace HomeworkCentral.Api.Assessment;

/// <summary>Reloads each isolated hashed-MLP chat monitor when another worker publishes its canonical generation.</summary>
public sealed class NeuralNetCheckpointRefreshService(IServiceScopeFactory scopes, IChatMonitoringNeuralModelFactory chatMonitoringModels, ILogger<NeuralNetCheckpointRefreshService> logger) : BackgroundService
{
    private readonly Dictionary<NeuralModelKindChatMonitoring, string> loadedChecksums = [];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Canonical neural checkpoint refresh failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
