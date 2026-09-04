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

        if (!TryBind(handle, "hc_embed_text", out EmbedTextNative? embedText)
            || !TryBind(handle, "hc_add_weighted_tokens", out AddWeightedTokensNative? addTokens)
            || !TryBind(handle, "hc_cosine", out CosineNative? cosine))
        {
            NativeLibrary.Free(handle);
            return;
        }

        EmbedTextFn = embedText;
        AddWeightedTokensFn = addTokens;
        CosineFn = cosine;
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
        List<string> existingPaths = CandidateLibraryPaths()
            .Where(static path => File.Exists(path))
            .ToList();

        for (int index = 0; index < existingPaths.Count; index++)
        {
            if (NativeLibrary.TryLoad(existingPaths[index], out nint handle))
                return handle;
        }

        return 0;
    }

    internal static IEnumerable<string> CandidateLibraryPaths()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
            yield break;

        string besideApp = JoinLibraryPath(baseDirectory, LibraryFileName);
        if (besideApp.Length > 0)
            yield return besideApp;

        string nativeBesideApp = JoinLibraryPath(baseDirectory, "native", LibraryFileName);
        if (nativeBesideApp.Length > 0)
            yield return nativeBesideApp;

        string? directory = baseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string debugPath = JoinLibraryPath(directory, "rust", "target", "debug", LibraryFileName);
            if (debugPath.Length > 0)
                yield return debugPath;

            string releasePath = JoinLibraryPath(directory, "rust", "target", "release", LibraryFileName);
            if (releasePath.Length > 0)
                yield return releasePath;

            directory = Directory.GetParent(directory)?.FullName;
        }
    }

    /// <summary>
    /// Appends relative kernel-library segments under a search root.
    /// <see cref="Path.Combine"/> discards earlier segments when a later
    /// argument is rooted; the native probe must keep the walked prefix.
    /// </summary>
    internal static string JoinLibraryPath(string root, params string[] relativeSegments)
    {
        if (string.IsNullOrEmpty(root))
            return string.Empty;

        string path = root;
        foreach (string segment in relativeSegments)
        {
            if (string.IsNullOrEmpty(segment) || Path.IsPathRooted(segment))
                return string.Empty;

            path = Path.Join(path, segment);
        }

        return path;
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
