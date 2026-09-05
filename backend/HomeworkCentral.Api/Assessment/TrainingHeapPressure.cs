using System.Diagnostics;

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

    public static TrainingHeapSample Sample()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long heap = Math.Max(info.HeapSizeBytes, GC.GetTotalMemory(forceFullCollection: false));
        long limit = info.TotalAvailableMemoryBytes;
        long rss = 0;
        using (Process process = Process.GetCurrentProcess())
            rss = process.WorkingSet64;

        if (info.MemoryLoadBytes > 0 && info.HighMemoryLoadThresholdBytes > 0
            && info.MemoryLoadBytes >= info.HighMemoryLoadThresholdBytes
            && limit > 0)
        {
            heap = Math.Max(heap, (long)(limit * SpillRatio));
        }

        return new TrainingHeapSample(
            heap,
            HighWatermarkBytes(limit),
            Math.Max(0, rss),
            Math.Max(0, limit));
    }

    public static long HighWatermarkBytes(long limitBytes) =>
        limitBytes <= 0 ? 0 : (long)(limitBytes * SpillRatio);

    public static bool ShouldSpill() => ShouldSpill(Sample());

    public static bool ShouldSkipTraces() => ShouldSkipTraces(Sample());

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
