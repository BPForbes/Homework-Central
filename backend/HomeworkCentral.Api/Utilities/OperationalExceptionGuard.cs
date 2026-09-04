using System.Data.Common;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace HomeworkCentral.Api.Utilities;

/// <summary>
/// Runs work while catching only operational/infrastructure failures (DB, IO, timeout,
/// serialization). Unexpected exceptions such as <see cref="NullReferenceException"/>
/// still propagate. Prefer this over <c>catch (Exception)</c> so CodeQL and reviewers
/// can see the closed failure set.
/// </summary>
public static class OperationalExceptionGuard
{
    public static async Task RunAsync(Func<Task> action, Func<Exception, Task> onFailure) =>
        await RunCoreAsync(action, onFailure, rethrow: false);

    /// <summary>
    /// Same closed catch set as <see cref="RunAsync"/>, but rethrows after
    /// <paramref name="onFailure"/> so callers can mark failure state then bubble.
    /// </summary>
    public static async Task RunObservingAsync(Func<Task> action, Func<Exception, Task> onFailure) =>
        await RunCoreAsync(action, onFailure, rethrow: true);

    private static async Task RunCoreAsync(
        Func<Task> action,
        Func<Exception, Task> onFailure,
        bool rethrow)
    {
        try
        {
            await action();
        }
        catch (DbException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (IOException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (SocketException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (TimeoutException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (InvalidOperationException ex)
        {
            // Includes ObjectDisposedException (derived type).
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (ArgumentException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (JsonException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (InvalidDataException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (NotSupportedException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (FormatException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (UnauthorizedAccessException ex)
        {
            await HandleAsync(ex, onFailure, rethrow);
        }
        catch (AggregateException ex)
        {
            await HandleAsync(UnwrapAggregateOrThrowCancellation(ex), onFailure, rethrow);
        }
    }

    private static async Task HandleAsync(
        Exception ex,
        Func<Exception, Task> onFailure,
        bool rethrow)
    {
        await onFailure(ex);
        if (!rethrow)
            return;

        ExceptionDispatchInfo.Capture(ex).Throw();
    }

    public static Task<T> RunAsync<T>(Func<Task<T>> action, Func<Exception, T> onFailure) =>
        RunAsync(action, ex => Task.FromResult(onFailure(ex)));

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, Func<Exception, Task<T>> onFailure)
    {
        try
        {
            return await action();
        }
        catch (DbException ex)
        {
            return await onFailure(ex);
        }
        catch (IOException ex)
        {
            return await onFailure(ex);
        }
        catch (SocketException ex)
        {
            return await onFailure(ex);
        }
        catch (TimeoutException ex)
        {
            return await onFailure(ex);
        }
        catch (InvalidOperationException ex)
        {
            // Includes ObjectDisposedException (derived type).
            return await onFailure(ex);
        }
        catch (ArgumentException ex)
        {
            return await onFailure(ex);
        }
        catch (JsonException ex)
        {
            return await onFailure(ex);
        }
        catch (InvalidDataException ex)
        {
            return await onFailure(ex);
        }
        catch (NotSupportedException ex)
        {
            return await onFailure(ex);
        }
        catch (FormatException ex)
        {
            return await onFailure(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return await onFailure(ex);
        }
        catch (AggregateException ex)
        {
            return await onFailure(UnwrapAggregateOrThrowCancellation(ex));
        }
    }

    private static Exception UnwrapAggregateOrThrowCancellation(AggregateException ex)
    {
        Exception inner = ex.Flatten().InnerException ?? ex;
        if (inner is OperationCanceledException)
            ExceptionDispatchInfo.Capture(inner).Throw();

        return inner;
    }

    public static async Task RunAsync(Func<Task> action, Action<Exception> onFailure) =>
        await RunAsync(action, ex =>
        {
            onFailure(ex);
            return Task.CompletedTask;
        });
}
