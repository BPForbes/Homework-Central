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
}

public sealed class ApplicationReadiness : IApplicationReadiness
{
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
}
