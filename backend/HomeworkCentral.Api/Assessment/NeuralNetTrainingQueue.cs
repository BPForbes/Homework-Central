using System.Runtime.CompilerServices;

namespace HomeworkCentral.Api.Assessment;

public interface INeuralNetTrainingQueue
{
    bool TryEnqueue(Guid sessionId);
    bool TryRemove(Guid sessionId);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// Bounded FIFO of queued (not yet claimed) session IDs. Unlike a <see cref="System.Threading.Channels.Channel{T}"/>,
/// this supports removing an entry that is still waiting, so a deleted queued session immediately frees its slot
/// instead of occupying capacity until the worker's single-threaded loop happens to drain it.
/// </summary>
public sealed class NeuralNetTrainingQueue : INeuralNetTrainingQueue
{
    private const int Capacity = 8;
    private readonly object gate = new();
    private readonly LinkedList<Guid> pending = [];
    private readonly SemaphoreSlim signal = new(0);

    public bool TryEnqueue(Guid sessionId)
    {
        lock (gate)
        {
            if (pending.Count >= Capacity) return false;
            pending.AddLast(sessionId);
        }
        signal.Release();
        return true;
    }

    public bool TryRemove(Guid sessionId)
    {
        lock (gate)
        {
            LinkedListNode<Guid>? node = pending.Find(sessionId);
            if (node is null) return false;
            pending.Remove(node);
            return true;
        }
    }

    public async IAsyncEnumerable<Guid> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            await signal.WaitAsync(ct);
            Guid? sessionId = null;
            lock (gate)
            {
                if (pending.Count > 0)
                {
                    sessionId = pending.First!.Value;
                    pending.RemoveFirst();
                }
            }
            // A removed entry still consumes the signal that was released when it was enqueued;
            // when that happens there is nothing to hand back, so loop around and wait again.
            if (sessionId is Guid value) yield return value;
        }
    }
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
