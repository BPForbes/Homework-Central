using System.Text;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Process-facing LRU. Prefers the Rust <c>hc_lru_*</c> exports
/// (<c>hc-cache</c> via <c>libhc_kernels</c>). Falls back to the managed
/// twin when the native library is missing or the exports are absent.
/// </summary>
internal sealed class HostLru
{
    private readonly nint native;
    private readonly LruCache<string, byte[]>? fallback;

    public HostLru(int capacity)
    {
        if (RustKernels.HasLru && RustKernels.TryLruCreate((nuint)capacity, out nint handle) && handle != 0)
        {
            native = handle;
            fallback = null;
        }
        else
        {
            native = 0;
            fallback = new LruCache<string, byte[]>(capacity);
        }
    }

    public bool IsNative => native != 0;

    public bool TryGetFloats(string key, out float[] values)
    {
        if (!TryGetBytes(key, out byte[] payload) || payload.Length % sizeof(float) != 0)
        {
            values = [];
            return false;
        }

        values = new float[payload.Length / sizeof(float)];
        Buffer.BlockCopy(payload, 0, values, 0, payload.Length);
        return true;
    }

    public void PutFloats(string key, float[] values)
    {
        byte[] payload = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, payload, 0, payload.Length);
        PutBytes(key, payload);
    }

    public bool TryGetBytes(string key, out byte[] value)
    {
        if (fallback is not null)
            return fallback.TryGet(key, out value!);

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        int status = RustKernels.TryLruGet(native, keyBytes, [], out int needed);
        if (status == 1)
        {
            value = [];
            return false;
        }

        if (status == 0 && needed == 0)
        {
            value = [];
            return true;
        }

        if (status != -3 && status != 0)
        {
            value = [];
            return false;
        }

        byte[] dest = new byte[needed];
        status = RustKernels.TryLruGet(native, keyBytes, dest, out _);
        if (status != 0)
        {
            value = [];
            return false;
        }

        value = dest;
        return true;
    }

    public void PutBytes(string key, byte[] value)
    {
        if (fallback is not null)
        {
            fallback.Put(key, value);
            return;
        }

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        _ = RustKernels.TryLruPut(native, keyBytes, value);
    }

    public void Clear()
    {
        if (fallback is not null)
            fallback.Clear();
        else
            RustKernels.LruClear(native);
    }
}
