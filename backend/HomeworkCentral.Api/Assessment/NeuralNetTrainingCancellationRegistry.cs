using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Per-session cancel tokens so administrators can stop a running synthetic training job
/// without shutting down the host. Continuous sessions rely on this for "train until canceled".
/// </summary>
public interface INeuralNetTrainingCancellationRegistry
{
    CancellationToken Link(Guid sessionId, CancellationToken hostToken);
    bool TryCancel(Guid sessionId);
    void Unregister(Guid sessionId);
}

public sealed class NeuralNetTrainingCancellationRegistry : INeuralNetTrainingCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sessions = new();

    public CancellationToken Link(Guid sessionId, CancellationToken hostToken)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        CancellationTokenSource held = _sessions.AddOrUpdate(
            sessionId,
            _ => linked,
            (_, previous) =>
            {
                previous.Dispose();
                return linked;
            });
        return held.Token;
    }

    public bool TryCancel(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out CancellationTokenSource? source))
            return false;

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Unregister(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out CancellationTokenSource? source))
            source.Dispose();
    }
}
