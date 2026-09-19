namespace HomeworkCentral.Api.Services;

/// <summary>Process-wide startup readiness for /healthz and gated API use.</summary>
public enum ApplicationReadyState
{
    Starting,
    Ready,
    Failed,
}

public interface IApplicationReadiness
{
    ApplicationReadyState State { get; }
    string? FailureMessage { get; }
    void MarkReady();
    void MarkFailed(string message);

    /// <summary>
    /// Waits until migrate/seed marks the process ready.
    /// Background workers must not query tenant tables before this returns true,
    /// or they race <c>__EFAppMigrationsHistory</c> and missing relations on a fresh volume.
    /// </summary>
    Task<bool> WaitUntilReadyAsync(CancellationToken ct);
}

public sealed class ApplicationReadiness : IApplicationReadiness
{
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly object _gate = new();
    private ApplicationReadyState _state = ApplicationReadyState.Starting;
    private string? _failureMessage;

    public ApplicationReadyState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public string? FailureMessage
    {
        get
        {
            lock (_gate)
                return _failureMessage;
        }
    }

    public void MarkReady()
    {
        lock (_gate)
        {
            _state = ApplicationReadyState.Ready;
            _failureMessage = null;
        }
    }

    public void MarkFailed(string message)
    {
        lock (_gate)
        {
            _state = ApplicationReadyState.Failed;
            _failureMessage = message;
        }
    }

    public async Task<bool> WaitUntilReadyAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            switch (State)
            {
                case ApplicationReadyState.Ready:
                    return true;
                case ApplicationReadyState.Failed:
                    return false;
                default:
                    await Task.Delay(ReadyPollInterval, ct);
                    break;
            }
        }
    }
}
