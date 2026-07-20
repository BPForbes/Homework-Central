using System.Threading.Channels;

namespace HomeworkCentral.Api.Assessment;

public interface INeuralNetTrainingQueue
{
    bool TryEnqueue(Guid sessionId);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}

public sealed class NeuralNetTrainingQueue : INeuralNetTrainingQueue
{
    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(8)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    public bool TryEnqueue(Guid sessionId) => channel.Writer.TryWrite(sessionId);
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

public sealed class NeuralNetTrainingWorker(
    INeuralNetTrainingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<NeuralNetTrainingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid sessionId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                INeuralNetTrainingService service = scope.ServiceProvider.GetRequiredService<INeuralNetTrainingService>();
                await service.RunSyntheticSessionAsync(sessionId, stoppingToken);
                NeuralNetTrainingPromoter promoter = scope.ServiceProvider.GetRequiredService<NeuralNetTrainingPromoter>();
                await promoter.PromoteNextAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Synthetic neural-net session {SessionId} failed.", sessionId);
            }
        }
    }
}
