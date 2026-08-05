using TorchSharp;
using static TorchSharp.torch;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// LibTorch device bind for accelerated hashed-MLP training.
/// Default package is <c>TorchSharp-cpu</c>; GPU hosts swap to
/// <c>TorchSharp-cuda-linux</c> / <c>TorchSharp-cuda-windows</c> and set
/// <see cref="NeuralNetTrainingOptions.TorchDevice"/> to <c>cuda</c> or <c>auto</c>.
/// Math.NET remains the checkpoint and Full-replay path.
/// </summary>
public static class NeuralTorchRuntime
{
    private static readonly object Gate = new();
    private static bool _configured;
    private static bool _preferAccelerated = true;
    private static string _devicePreference = "auto";
    private static bool _ready;
    private static bool _available;
    private static Device _device = CPU;
    private static string _backendLabel = "mathnet-cpu";
    private static string? _lastFailure;

    public static bool PreferAccelerated
    {
        get { lock (Gate) return _preferAccelerated; }
    }

    public static bool IsAvailable
    {
        get { lock (Gate) return _available; }
    }

    public static string BackendLabel
    {
        get { lock (Gate) return _backendLabel; }
    }

    public static string? LastFailure
    {
        get { lock (Gate) return _lastFailure; }
    }

    public static Device Device
    {
        get { lock (Gate) return _device; }
    }

    /// <summary>Apply training options; safe to call more than once (tests / host restart).</summary>
    public static void Configure(NeuralNetTrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (Gate)
        {
            _preferAccelerated = options.PreferTorchAccelerator;
            _devicePreference = string.IsNullOrWhiteSpace(options.TorchDevice)
                ? "auto"
                : options.TorchDevice.Trim().ToLowerInvariant();
            _configured = true;
            _ready = false;
            _available = false;
            _lastFailure = null;
            _backendLabel = "mathnet-cpu";
            _device = CPU;
        }
    }

    /// <summary>
    /// Lazily loads LibTorch and selects CUDA when requested and present.
    /// Returns false when acceleration is disabled or native load fails.
    /// </summary>
    public static bool TryEnsureReady()
    {
        lock (Gate)
        {
            if (!_configured)
            {
                _preferAccelerated = true;
                _devicePreference = "auto";
                _configured = true;
            }

            if (!_preferAccelerated)
            {
                _available = false;
                _backendLabel = "mathnet-cpu";
                return false;
            }

            if (_ready)
                return _available;

            try
            {
                bool wantCuda = _devicePreference switch
                {
                    "cuda" => true,
                    "cpu" => false,
                    _ => true,
                };

                if (wantCuda && cuda.is_available())
                {
                    _device = CUDA;
                    _backendLabel = "torch-cuda";
                }
                else
                {
                    if (_devicePreference == "cuda")
                        _lastFailure = "TorchDevice=cuda but cuda.is_available() is false; using CPU LibTorch.";
                    _device = CPU;
                    _backendLabel = "torch-cpu";
                    // Touch a tiny tensor so missing native binaries fail here, not mid-epoch.
                    using Tensor probe = zeros(1, device: _device);
                    _ = probe.item<float>();
                }

                _available = true;
                _ready = true;
                return true;
            }
            catch (Exception exception)
            {
                _available = false;
                _ready = true;
                _backendLabel = "mathnet-cpu";
                _lastFailure = exception.Message;
                return false;
            }
        }
    }
}
