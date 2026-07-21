namespace HomeworkCentral.Api.Assessment;

/// <summary>Reloads the singleton inference model when another worker publishes a canonical generation.</summary>
public sealed class NeuralNetCheckpointRefreshService(IServiceScopeFactory scopes, ITicketStudentModel model, ILogger<NeuralNetCheckpointRefreshService> logger) : BackgroundService
{
    private string? loadedChecksum;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                NeuralNetCheckpointStore store = scope.ServiceProvider.GetRequiredService<NeuralNetCheckpointStore>();
                HomeworkCentral.Api.Models.NeuralNetCanonicalCheckpoint? checkpoint = await store.GetCurrentAsync(stoppingToken);
                if (checkpoint is not null && checkpoint.Checksum != loadedChecksum && model is ITicketStudentModelTelemetry telemetry)
                {
                    telemetry.LoadParameterSnapshot(new(checkpoint.Generation, 0, "ieee754-float32-le", "dense-base64", 2074, checkpoint.ParametersBase64, checkpoint.Checksum));
                    loadedChecksum = checkpoint.Checksum;
                    logger.LogInformation("Loaded canonical neural checkpoint {Generation}.", checkpoint.Generation);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Canonical neural checkpoint refresh failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
