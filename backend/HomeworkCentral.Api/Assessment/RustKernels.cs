using System.Runtime.InteropServices;
using System.Text;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Loads <c>libhc_kernels</c> for lexical bins, store cosine, GEMV,
/// expertise hash, HashEmbed, JSON batch cosine, support-set cosine,
/// heap-pressure watermarks, and bounded top-K mesh extraction.
/// Encode metadata, hashed-MLP train/replay, and the rest of the API stay
/// in C#. The API image does not ship rustc; managed implementations run
/// when the native library is missing or a newer export is absent.
/// </summary>
internal static class RustKernels
{
    internal const int HashEmbedBinCount = 64;

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
    private static readonly GemvBiasNative? GemvBiasFn;
    private static readonly GemvTransposeNative? GemvTransposeFn;
    private static readonly AddExpertiseHashNative? AddExpertiseHashFn;
    private static readonly HashEmbedNative? HashEmbedFn;
    private static readonly CosineNative? SupportCosineFn;
    private static readonly SupportMaxCosineNative? SupportMaxCosineFn;
    private static readonly BatchCosineJsonNative? BatchCosineJsonFn;
    private static readonly HeapShouldSpillNative? HeapShouldSpillFn;
    private static readonly HeapTopKAbsNative? HeapTopKAbsFn;

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
        TryBind(handle, "hc_gemv_bias", out GemvBiasNative? gemvBias);
        TryBind(handle, "hc_gemv_transpose", out GemvTransposeNative? gemvTranspose);
        TryBind(handle, "hc_add_expertise_hash", out AddExpertiseHashNative? addExpertise);
        TryBind(handle, "hc_hash_embed", out HashEmbedNative? hashEmbed);
        TryBind(handle, "hc_support_cosine", out CosineNative? supportCosine);
        TryBind(handle, "hc_support_max_cosine", out SupportMaxCosineNative? supportMax);
        TryBind(handle, "hc_batch_cosine_json", out BatchCosineJsonNative? batchCosine);
        TryBind(handle, "hc_heap_should_spill", out HeapShouldSpillNative? heapSpill);
        TryBind(handle, "hc_heap_top_k_abs", out HeapTopKAbsNative? heapTopK);
        GemvBiasFn = gemvBias;
        GemvTransposeFn = gemvTranspose;
        AddExpertiseHashFn = addExpertise;
        HashEmbedFn = hashEmbed;
        SupportCosineFn = supportCosine;
        SupportMaxCosineFn = supportMax;
        BatchCosineJsonFn = batchCosine;
        HeapShouldSpillFn = heapSpill;
        HeapTopKAbsFn = heapTopK;
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

    internal static unsafe bool TryMultiplyBias(
        float[] weightsColumnMajor,
        int rows,
        int cols,
        float[] source,
        float[] biases,
        float[] destination)
    {
        if (GemvBiasFn is null || rows <= 0 || cols <= 0)
            return false;

        int weightCount;
        try
        {
            weightCount = checked(rows * cols);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (weightsColumnMajor.Length < weightCount
            || source.Length < cols
            || biases.Length < rows
            || destination.Length < rows)
        {
            return false;
        }

        fixed (float* weightsPointer = weightsColumnMajor)
        fixed (float* sourcePointer = source)
        fixed (float* biasPointer = biases)
        fixed (float* destinationPointer = destination)
        {
            int status = GemvBiasFn(
                weightsPointer,
                (nuint)rows,
                (nuint)cols,
                sourcePointer,
                biasPointer,
                destinationPointer);
            return status == 0;
        }
    }

    internal static unsafe bool TryMultiplyTranspose(
        float[] weightsColumnMajor,
        int rows,
        int cols,
        float[] delta,
        float[] destination)
    {
        if (GemvTransposeFn is null || rows <= 0 || cols <= 0)
            return false;

        int weightCount;
        try
        {
            weightCount = checked(rows * cols);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (weightsColumnMajor.Length < weightCount
            || delta.Length < rows
            || destination.Length < cols)
        {
            return false;
        }

        fixed (float* weightsPointer = weightsColumnMajor)
        fixed (float* deltaPointer = delta)
        fixed (float* destinationPointer = destination)
        {
            int status = GemvTransposeFn(
                weightsPointer,
                (nuint)rows,
                (nuint)cols,
                deltaPointer,
                destinationPointer);
            return status == 0;
        }
    }

    internal static unsafe bool TryAddExpertiseHash(
        float[] values,
        string label,
        int baseInputSize,
        int binCount)
    {
        if (AddExpertiseHashFn is null || values.Length == 0 || binCount <= 0 || baseInputSize < 0)
            return false;

        int required;
        try
        {
            required = checked(baseInputSize + binCount);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (values.Length < required)
            return false;

        byte[] utf8 = Encoding.UTF8.GetBytes(label);
        fixed (float* valuesPointer = values)
        fixed (byte* labelPointer = utf8)
        {
            int status = AddExpertiseHashFn(
                valuesPointer,
                (nuint)values.Length,
                labelPointer,
                (nuint)utf8.Length,
                (nuint)baseInputSize,
                (nuint)binCount);
            return status == 0;
        }
    }

    internal static unsafe bool TryHashEmbed(string text, out float[] vector)
    {
        vector = [];
        if (HashEmbedFn is null)
            return false;

        float[] output = new float[HashEmbedBinCount];
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        fixed (byte* textPointer = utf8)
        fixed (float* outputPointer = output)
        {
            int status = HashEmbedFn(
                textPointer,
                (nuint)utf8.Length,
                outputPointer,
                (nuint)output.Length);
            if (status != 0)
                return false;
        }

        vector = output;
        return true;
    }

    internal static unsafe bool TrySupportCosine(IReadOnlyList<float> left, IReadOnlyList<float> right, out double score)
    {
        score = 0;
        if (SupportCosineFn is null)
            return false;

        float[] leftArray = ToArray(left);
        float[] rightArray = ToArray(right);
        if (leftArray.Length == 0 || rightArray.Length == 0)
            return true;

        fixed (float* leftPointer = leftArray)
        fixed (float* rightPointer = rightArray)
        {
            score = SupportCosineFn(
                leftPointer,
                (nuint)leftArray.Length,
                rightPointer,
                (nuint)rightArray.Length);
        }

        return true;
    }

    internal static unsafe bool TryMaxSupportCosine(
        float[] query,
        IEnumerable<float[]> supportVectors,
        out double score)
    {
        score = 0;
        if (SupportMaxCosineFn is null)
            return false;

        List<float[]> vectors = supportVectors as List<float[]> ?? supportVectors.ToList();
        if (vectors.Count == 0 || query.Length == 0)
            return true;

        int packedCount = vectors.Sum(static vector => vector.Length);
        float[] packed = new float[packedCount];
        nuint[] lengths = new nuint[vectors.Count];
        int offset = 0;
        for (int index = 0; index < vectors.Count; index++)
        {
            float[] vector = vectors[index];
            lengths[index] = (nuint)vector.Length;
            Array.Copy(vector, 0, packed, offset, vector.Length);
            offset += vector.Length;
        }

        fixed (float* queryPointer = query)
        fixed (float* packedPointer = packed)
        fixed (nuint* lengthsPointer = lengths)
        {
            score = SupportMaxCosineFn(
                queryPointer,
                (nuint)query.Length,
                packedPointer,
                (nuint)packed.Length,
                lengthsPointer,
                (nuint)vectors.Count);
        }

        return true;
    }

    internal static unsafe bool TryBatchCosineJson(
        IReadOnlyList<float> query,
        IEnumerable<string> embeddingJson,
        out double[] scores)
    {
        scores = [];
        if (BatchCosineJsonFn is null)
            return false;

        List<string> documents = embeddingJson as List<string> ?? embeddingJson.ToList();
        if (documents.Count == 0)
            return true;

        float[] queryArray = ToArray(query);
        byte[][] encoded = documents.Select(static json => Encoding.UTF8.GetBytes(json ?? "")).ToArray();
        int totalBytes = encoded.Sum(static blob => blob.Length);
        byte[] blob = new byte[totalBytes];
        nuint[] lengths = new nuint[encoded.Length];
        int offset = 0;
        for (int index = 0; index < encoded.Length; index++)
        {
            byte[] part = encoded[index];
            lengths[index] = (nuint)part.Length;
            Buffer.BlockCopy(part, 0, blob, offset, part.Length);
            offset += part.Length;
        }

        double[] nativeScores = new double[documents.Count];
        fixed (float* queryPointer = queryArray)
        fixed (byte* jsonPointer = blob)
        fixed (nuint* lengthsPointer = lengths)
        fixed (double* scoresPointer = nativeScores)
        {
            float* queryArgument = queryArray.Length == 0 ? null : queryPointer;
            byte* jsonArgument = blob.Length == 0 ? null : jsonPointer;
            int status = BatchCosineJsonFn(
                queryArgument,
                (nuint)queryArray.Length,
                jsonArgument,
                (nuint)blob.Length,
                lengthsPointer,
                (nuint)documents.Count,
                scoresPointer);
            if (status != 0)
                return false;
        }

        scores = nativeScores;
        return true;
    }

    internal static bool TryShouldSpill(
        long usedBytes,
        long highWatermarkBytes,
        long processRssBytes,
        long processLimitBytes,
        out bool spill)
    {
        spill = false;
        if (HeapShouldSpillFn is null)
            return false;

        int status = HeapShouldSpillFn(usedBytes, highWatermarkBytes, processRssBytes, processLimitBytes);
        if (status < 0)
            return false;

        spill = status > 0;
        return true;
    }

    internal static unsafe bool TryTopKAbs(
        ReadOnlySpan<float> values,
        ReadOnlySpan<int> indexes,
        int take,
        out int[] selectedIndexes)
    {
        selectedIndexes = [];
        if (HeapTopKAbsFn is null || take <= 0)
            return false;
        if (values.Length != indexes.Length)
            return false;
        if (values.Length == 0)
            return true;

        int[] outIndexes = new int[take];
        float[] outValues = new float[take];
        int written;
        fixed (float* valuesPointer = values)
        fixed (int* indexesPointer = indexes)
        fixed (int* outIndexPointer = outIndexes)
        fixed (float* outValuePointer = outValues)
        {
            written = HeapTopKAbsFn(
                valuesPointer,
                indexesPointer,
                (nuint)values.Length,
                (nuint)take,
                outIndexPointer,
                outValuePointer);
        }

        if (written < 0)
            return false;

        if (written == 0)
            return true;

        selectedIndexes = outIndexes[..written];
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int GemvBiasNative(
        float* weights,
        nuint rows,
        nuint cols,
        float* source,
        float* biases,
        float* destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int GemvTransposeNative(
        float* weights,
        nuint rows,
        nuint cols,
        float* delta,
        float* destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int AddExpertiseHashNative(
        float* values,
        nuint valuesLength,
        byte* label,
        nuint labelLength,
        nuint baseInputSize,
        nuint binCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int HashEmbedNative(byte* text, nuint textLength, float* output, nuint outputLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate double SupportMaxCosineNative(
        float* query,
        nuint queryLength,
        float* packed,
        nuint packedLength,
        nuint* lengths,
        nuint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HeapShouldSpillNative(
        long usedBytes,
        long highWatermarkBytes,
        long processRssBytes,
        long processLimitBytes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int HeapTopKAbsNative(
        float* values,
        int* indexes,
        nuint count,
        nuint take,
        int* outIndexes,
        float* outValues);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int BatchCosineJsonNative(
        float* query,
        nuint queryLength,
        byte* json,
        nuint jsonLength,
        nuint* lengths,
        nuint documentCount,
        double* scores);
}
