using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Risk;

/// <summary>
/// In-memory (per-instance) implementation keyed by identity, evicted after a long idle period.
/// For a multi-instance production deployment this state should move to a shared store (Redis,
/// or a table) so trust history is consistent across instances — see the note in the module's
/// summary; kept in <see cref="IMemoryCache"/> here for consistency with the rest of the captcha
/// and scraping-detection modules, which make the same instance-local tradeoff.
/// </summary>
public sealed class IdentityRiskProfileService(IMemoryCache cache, IOptions<RiskOptions> options) : IIdentityRiskProfileService
{
    private const double NeutralTrust = 0.5;
    private static readonly TimeSpan IdleExpiration = TimeSpan.FromDays(30);
    private static readonly ConcurrentDictionary<string, object> CreationLocks = new();

    public IdentityRiskProfile GetProfile(string identity)
    {
        ProfileState state = GetOrCreate(identity);
        lock (state.Lock)
            return Snapshot(state);
    }

    public void RecordOutcome(string identity, bool passed, double observedScore)
    {
        RiskOptions opts = options.Value;
        ProfileState state = GetOrCreate(identity);

        lock (state.Lock)
        {
            DateTime now = DateTime.UtcNow;
            state.TotalScans++;
            state.ConsecutiveFailures = passed ? 0 : state.ConsecutiveFailures + 1;
            if (passed)
                state.SuccessfulScans++;

            bool isFirstUpdate = state.LastTrustUpdateUtc == default;
            TimeSpan sinceLastUpdate = now - state.LastTrustUpdateUtc;
            if (!isFirstUpdate && sinceLastUpdate < TimeSpan.FromSeconds(opts.MinProfileUpdateIntervalSeconds))
                return;

            double decayed = isFirstUpdate
                ? state.TrustScore
                : DecayTowardNeutral(state.TrustScore, state.LastTrustUpdateUtc, now, opts.TrustDecayHalfLifeHours);

            state.TrustScore = Math.Clamp((decayed * (1 - opts.TrustEmaAlpha)) + (observedScore * opts.TrustEmaAlpha), 0.0, 1.0);
            state.LastTrustUpdateUtc = now;
        }
    }

    private static double DecayTowardNeutral(double trust, DateTime lastUpdateUtc, DateTime now, int halfLifeHours)
    {
        if (halfLifeHours <= 0)
            return trust;

        double hoursElapsed = (now - lastUpdateUtc).TotalHours;
        if (hoursElapsed <= 0)
            return trust;

        double decayFactor = Math.Pow(0.5, hoursElapsed / halfLifeHours);
        return NeutralTrust + ((trust - NeutralTrust) * decayFactor);
    }

    private ProfileState GetOrCreate(string identity)
    {
        string cacheKey = CacheKey(identity);
        if (cache.TryGetValue(cacheKey, out ProfileState? existing) && existing is not null)
            return existing;

        object creationLock = CreationLocks.GetOrAdd(identity, _ => new object());
        lock (creationLock)
        {
            if (cache.TryGetValue(cacheKey, out ProfileState? created) && created is not null)
                return created;

            return cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SlidingExpiration = IdleExpiration;
                return new ProfileState();
            })!;
        }
    }

    private static IdentityRiskProfile Snapshot(ProfileState state) =>
        new(state.TrustScore, state.TotalScans, state.SuccessfulScans, state.ConsecutiveFailures, state.LastTrustUpdateUtc);

    private static string CacheKey(string identity) => $"risk-profile:{identity}";

    private sealed class ProfileState
    {
        public readonly object Lock = new();
        public double TrustScore = NeutralTrust;
        public int TotalScans;
        public int SuccessfulScans;
        public int ConsecutiveFailures;
        public DateTime LastTrustUpdateUtc;
    }
}
