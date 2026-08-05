using System.Runtime.InteropServices;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Closed failure set for LibTorch bind and batched backward. Unexpected bugs still
/// propagate; these types map to "fall back to Math.NET" rather than abort training.
/// Prefer this over <c>catch (Exception)</c> so CodeQL sees the accelerator boundary.
/// </summary>
internal static class NeuralTorchAcceleratorGuard
{
    public static bool TryRun(Action action, Action<Exception>? onFailure = null)
    {
        try
        {
            action();
            return true;
        }
        catch (NotSupportedException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (DllNotFoundException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (BadImageFormatException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (TypeInitializationException exception)
        {
            // TorchSharp static ctor wraps missing-native NotSupportedException here.
            onFailure?.Invoke(exception);
            return false;
        }
        catch (TypeLoadException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (ArgumentException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (OutOfMemoryException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (ExternalException exception)
        {
            onFailure?.Invoke(exception);
            return false;
        }
        catch (AggregateException exception)
        {
            Exception inner = exception.Flatten().InnerException ?? exception;
            if (inner is OperationCanceledException)
                throw;
            if (!IsAcceleratorFailure(inner))
                throw;
            onFailure?.Invoke(inner);
            return false;
        }
    }

    public static bool TryRun<T>(Func<T> action, out T result, Action<Exception>? onFailure = null)
    {
        T? captured = default;
        bool ok = TryRun(
            () => { captured = action(); },
            onFailure);
        result = captured!;
        return ok;
    }

    private static bool IsAcceleratorFailure(Exception exception) =>
        exception is NotSupportedException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or TypeInitializationException
            or TypeLoadException
            or InvalidOperationException
            or ArgumentException
            or OutOfMemoryException
            or ExternalException;
}
