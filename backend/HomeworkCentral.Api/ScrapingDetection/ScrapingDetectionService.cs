using Microsoft.Extensions.Caching.Memory;

namespace HomeworkCentral.Api.ScrapingDetection;

/// <summary>
/// Keeps a rolling <see cref="Window"/> of recent request samples per identity (in-memory, evicted
/// after <see cref="IdleExpiration"/> of inactivity) and scores each new request against four
/// additive, explainable heuristics — same weighted-delta design as
/// <c>HomeworkCentral.Api.Captcha.BehaviorScoringService</c>, applied to request patterns instead
/// of mouse/keyboard telemetry.
/// </summary>
public sealed class ScrapingDetectionService(IMemoryCache cache) : IScrapingDetectionService
{
    private const double BlockThreshold = 0.75;
    private const int MinSamplesBeforeAssessing = 10;
    private const int MaxSamplesPerIdentity = 300;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan IdleExpiration = TimeSpan.FromMinutes(10);

    public ScrapingAssessment RecordRequest(ScrapingRequestSample sample)
    {
        IdentityWindow window = cache.GetOrCreate(CacheKey(sample.Identity), entry =>
        {
            entry.SlidingExpiration = IdleExpiration;
            return new IdentityWindow();
        })!;

        List<RequestRecord> snapshot;
        lock (window.Lock)
        {
            window.Samples.Add(new RequestRecord(sample.TimestampUtc, sample.Path, IsWriteMethod(sample.Method)));

            DateTime cutoff = sample.TimestampUtc - Window;
            window.Samples.RemoveAll(s => s.TimestampUtc < cutoff);

            if (window.Samples.Count > MaxSamplesPerIdentity)
                window.Samples.RemoveRange(0, window.Samples.Count - MaxSamplesPerIdentity);

            snapshot = [.. window.Samples];
        }

        return Assess(snapshot, sample.TimestampUtc);
    }

    private static ScrapingAssessment Assess(List<RequestRecord> samples, DateTime nowUtc)
    {
        if (samples.Count < MinSamplesBeforeAssessing)
            return new ScrapingAssessment(0, false, null);

        DateTime oneMinuteAgo = nowUtc.AddMinutes(-1);
        int requestsLastMinute = samples.Count(s => s.TimestampUtc >= oneMinuteAgo);
        int distinctPaths = samples.Select(s => s.Path).Distinct(StringComparer.Ordinal).Count();
        double writeRatio = samples.Count(s => s.IsWrite) / (double)samples.Count;
        double breadthRatio = distinctPaths / (double)samples.Count;

        double score = 0;
        List<string> reasons = [];

        // A sustained burst of requests is far beyond anything a human clicking through the UI
        // produces (there is no legitimate polling loop in this app — chat is push-based over
        // SignalR, not polled).
        if (requestsLastMinute > 90)
        {
            score += 0.3;
            reasons.Add($"{requestsLastMinute} requests in the last minute");
        }

        // Scraping is fundamentally enumeration: hitting many distinct resource URLs (e.g. every
        // chat room's message history) rather than revisiting the same handful a normal session
        // touches.
        if (breadthRatio > 0.6)
        {
            score += 0.2;
            reasons.Add($"{distinctPaths} distinct endpoints across {samples.Count} requests");
        }

        // A real user reading also writes sometimes (sends a message, claims a subject, ...); a
        // purely read-only identity at real volume looks like a crawler, not a person.
        if (writeRatio < 0.05 && requestsLastMinute > 30)
        {
            score += 0.2;
            reasons.Add("high volume with almost no write requests");
        }

        double? coefficientOfVariation = IntervalCoefficientOfVariation(samples);
        if (coefficientOfVariation is { } cov && cov < 0.15)
        {
            score += 0.2;
            reasons.Add("suspiciously uniform request timing");
        }

        score = Math.Clamp(score, 0.0, 1.0);
        return new ScrapingAssessment(score, score >= BlockThreshold, reasons.Count > 0 ? string.Join("; ", reasons) : null);
    }

    private static double? IntervalCoefficientOfVariation(List<RequestRecord> samples)
    {
        if (samples.Count < MinSamplesBeforeAssessing)
            return null;

        List<double> gapsMs = new(samples.Count - 1);
        for (int i = 1; i < samples.Count; i++)
            gapsMs.Add((samples[i].TimestampUtc - samples[i - 1].TimestampUtc).TotalMilliseconds);

        double mean = gapsMs.Average();
        if (mean <= 0 || mean > 2000)
            return null; // slow/idle traffic isn't scored on timing uniformity at all

        double variance = gapsMs.Select(g => (g - mean) * (g - mean)).Average();
        return Math.Sqrt(variance) / mean;
    }

    private static bool IsWriteMethod(string method) =>
        method is "POST" or "PUT" or "PATCH" or "DELETE";

    private static string CacheKey(string identity) => $"scrape:{identity}";

    private sealed record RequestRecord(DateTime TimestampUtc, string Path, bool IsWrite);

    private sealed class IdentityWindow
    {
        public readonly object Lock = new();
        public readonly List<RequestRecord> Samples = [];
    }
}
