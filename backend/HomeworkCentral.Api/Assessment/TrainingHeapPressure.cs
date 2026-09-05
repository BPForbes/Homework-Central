using System.Globalization;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Samples the CLR heap and process working set, then asks the Rust watermark
/// (or a matching C# fallback) whether training should spill to PostgreSQL.
/// Rust does not read the .NET GC heap — C# is the only source of those numbers.
/// </summary>
public static class TrainingHeapPressure
{
    /// <summary>Skip mesh/forward traces before the spill watermark so extraction never races OOM.</summary>
    public const double SkipTraceRatio = 0.55;

    /// <summary>Persist weights and empty in-memory traces at this fraction of available memory.</summary>
    public const double SpillRatio = 0.70;

    private const int SampleCacheMs = 250;

    private static int awaitingRelief;
    private static TrainingHeapSample cachedSample;
    private static long cachedAtMs = long.MinValue;

    public static TrainingHeapSample Sample()
    {
        long now = Environment.TickCount64;
        if (cachedAtMs != long.MinValue && now - cachedAtMs >= 0 && now - cachedAtMs < SampleCacheMs)
            return cachedSample;

        TrainingHeapSample sample = SampleUncached();
        cachedSample = sample;
        cachedAtMs = now;
        return sample;
    }

    internal static TrainingHeapSample SampleUncached()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long heap = Math.Max(info.HeapSizeBytes, GC.GetTotalMemory(forceFullCollection: false));
        long limit = info.TotalAvailableMemoryBytes;
        long rss = Math.Max(0, Environment.WorkingSet);

        if (TryParseHeapHardLimit(Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimit"), out long hardLimit)
            && (limit <= 0 || hardLimit < limit))
        {
            limit = hardLimit;
        }

        if (limit <= 0)
        {
            _ = GC.GetTotalMemory(forceFullCollection: true);
            info = GC.GetGCMemoryInfo();
            heap = Math.Max(info.HeapSizeBytes, GC.GetTotalMemory(forceFullCollection: false));
            limit = info.TotalAvailableMemoryBytes;
        }

        if (info.MemoryLoadBytes > 0 && info.HighMemoryLoadThresholdBytes > 0
            && info.MemoryLoadBytes >= info.HighMemoryLoadThresholdBytes
            && limit > 0)
        {
            heap = Math.Max(heap, (long)(limit * SpillRatio));
        }

        return new TrainingHeapSample(
            heap,
            HighWatermarkBytes(limit),
            rss,
            Math.Max(0, limit));
    }

    /// <summary>
    /// <c>DOTNET_GCHeapHardLimit</c> may be decimal or hex (optional <c>0x</c> prefix).
    /// </summary>
    internal static bool TryParseHeapHardLimit(string? raw, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        ReadOnlySpan<char> text = raw.AsSpan().Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes)
                && bytes > 0;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes) && bytes > 0)
            return true;

        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes)
            && bytes > 0;
    }

    public static long HighWatermarkBytes(long limitBytes) =>
        limitBytes <= 0 ? 0 : (long)(limitBytes * SpillRatio);

    public static bool ShouldSpill() => ShouldSpill(Sample());

    public static bool ShouldSkipTraces() => ShouldSkipTraces(Sample());

    /// <summary>
    /// Edge-triggered spill: after a successful persist, wait until the heap falls
    /// below the skip-trace line so mid-run SQL happens once per pressure wave.
    /// </summary>
    public static bool ShouldAttemptSpill() => ShouldAttemptSpill(Sample());

    public static bool ShouldAttemptSpill(TrainingHeapSample sample)
    {
        if (Volatile.Read(ref awaitingRelief) != 0)
        {
            if (!ShouldSkipTraces(sample))
                Volatile.Write(ref awaitingRelief, 0);
            else
                return false;
        }

        return ShouldSpill(sample);
    }

    public static void NoteSuccessfulSpill() => Volatile.Write(ref awaitingRelief, 1);

    internal static void ResetForTests()
    {
        Volatile.Write(ref awaitingRelief, 0);
        cachedAtMs = long.MinValue;
        cachedSample = default;
    }

    public static bool ShouldSpill(TrainingHeapSample sample)
    {
        if (RustKernels.TryShouldSpill(
            sample.HeapBytes,
            sample.HighWatermarkBytes,
            sample.RssBytes,
            sample.LimitBytes,
            out bool rustSpill))
        {
            return rustSpill;
        }

        return DecideSpill(
            sample.HeapBytes,
            sample.HighWatermarkBytes,
            sample.RssBytes,
            sample.LimitBytes);
    }

    public static bool ShouldSkipTraces(TrainingHeapSample sample)
    {
        if (ShouldSpill(sample))
            return true;

        if (sample.LimitBytes > 0 && sample.HeapBytes >= (long)(sample.LimitBytes * SkipTraceRatio))
            return true;

        return false;
    }

    /// <summary>Pure spill rule shared with <c>hc_heap_should_spill</c> when the native library is absent.</summary>
    public static bool DecideSpill(
        long usedBytes,
        long highWatermarkBytes,
        long processRssBytes,
        long processLimitBytes)
    {
        if (usedBytes < 0 || highWatermarkBytes < 0 || processRssBytes < 0 || processLimitBytes < 0)
            return false;

        if (highWatermarkBytes > 0 && usedBytes >= highWatermarkBytes)
            return true;

        return processLimitBytes > 0
            && processRssBytes >= (long)(processLimitBytes * SpillRatio);
    }
}

/// <summary>One CLR / process memory sample used to decide skip-trace vs spill.</summary>
public readonly record struct TrainingHeapSample(
    long HeapBytes,
    long HighWatermarkBytes,
    long RssBytes,
    long LimitBytes);
