using System.Runtime.InteropServices;
using System.Text;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Loads <c>libhc_kernels</c> for the lexical bins and store cosine only.
/// Encode metadata, the hashed MLP, and the rest of the API stay in C#.
/// The API image does not ship rustc; managed implementations run when
/// the native library is missing (CI csharp job, Docker publish).
/// </summary>
internal static class RustKernels
{
    internal static string LibraryFileName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "hc_kernels.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libhc_kernels.dylib";
            return "libhc_kernels.so";
        }
    }

    private static readonly EmbedTextNative? EmbedTextFn;
    private static readonly AddWeightedTokensNative? AddWeightedTokensFn;
    private static readonly CosineNative? CosineFn;

    internal static bool IsLoaded { get; }

    static RustKernels()
    {
        nint handle = TryOpenLibrary();
        if (handle == 0)
            return;

        if (!TryBind(handle, "hc_embed_text", out EmbedTextFn)
            || !TryBind(handle, "hc_add_weighted_tokens", out AddWeightedTokensFn)
            || !TryBind(handle, "hc_cosine", out CosineFn))
        {
            return;
        }

        IsLoaded = true;
    }

    internal static unsafe bool TryEmbedText(string text, Span<float> destination)
    {
        if (EmbedTextFn is null
            || destination.Length < ChatMonitoringFeatureEncoder.StructuralFeatureCount)
        {
            return false;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        fixed (byte* textPointer = utf8)
        fixed (float* outputPointer = destination)
        {
            int status = EmbedTextFn(
                textPointer,
                (nuint)utf8.Length,
                outputPointer,
                (nuint)destination.Length);
            return status == 0;
        }
    }

    internal static unsafe bool TryAddWeightedTokens(Span<float> values, string text, float weight)
    {
        if (AddWeightedTokensFn is null
            || values.Length < ChatMonitoringFeatureEncoder.StructuralFeatureCount)
        {
            return false;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        fixed (byte* textPointer = utf8)
        fixed (float* valuesPointer = values)
        {
            int status = AddWeightedTokensFn(
                valuesPointer,
                (nuint)values.Length,
                textPointer,
                (nuint)utf8.Length,
                weight);
            return status == 0;
        }
    }

    internal static unsafe bool TryCosine(IReadOnlyList<float> left, IReadOnlyList<float> right, out double score)
    {
        score = 0;
        if (CosineFn is null)
            return false;

        float[] leftArray = ToArray(left);
        float[] rightArray = ToArray(right);
        if (leftArray.Length == 0 || rightArray.Length == 0)
            return true;

        fixed (float* leftPointer = leftArray)
        fixed (float* rightPointer = rightArray)
        {
            score = CosineFn(
                leftPointer,
                (nuint)leftArray.Length,
                rightPointer,
                (nuint)rightArray.Length);
        }

        return true;
    }

    private static float[] ToArray(IReadOnlyList<float> values)
    {
        if (values is float[] array)
            return array;

        float[] copy = new float[values.Count];
        for (int index = 0; index < copy.Length; index++)
            copy[index] = values[index];
        return copy;
    }

    private static bool TryBind<T>(nint handle, string exportName, out T? function)
        where T : Delegate
    {
        function = null;
        if (!NativeLibrary.TryGetExport(handle, exportName, out nint address))
            return false;

        function = Marshal.GetDelegateForFunctionPointer<T>(address);
        return true;
    }

    private static nint TryOpenLibrary()
    {
        foreach (string path in CandidateLibraryPaths())
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
                return handle;
        }

        return 0;
    }

    private static IEnumerable<string> CandidateLibraryPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, LibraryFileName);
        yield return Path.Combine(AppContext.BaseDirectory, "native", LibraryFileName);

        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            yield return Path.Combine(directory, "rust", "target", "debug", LibraryFileName);
            yield return Path.Combine(directory, "rust", "target", "release", LibraryFileName);
            directory = Directory.GetParent(directory)?.FullName;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int EmbedTextNative(byte* text, nuint textLength, float* output, nuint outputLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int AddWeightedTokensNative(
        float* values,
        nuint valuesLength,
        byte* text,
        nuint textLength,
        float weight);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate double CosineNative(float* left, nuint leftLength, float* right, nuint rightLength);
}
