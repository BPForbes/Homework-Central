using HomeworkCentral.Api.Risk;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeworkCentral.Api.Tests.Risk;

public class IdentityRiskProfileServiceTests
{
    [Fact]
    public void A_never_seen_identity_starts_at_neutral_trust_with_no_history()
    {
        IdentityRiskProfileService service = CreateService();
        IdentityRiskProfile profile = service.GetProfile("user:new");

        Assert.Equal(0.5, profile.TrustScore);
        Assert.Equal(0, profile.TotalScans);
        Assert.Equal(0, profile.SuccessfulScans);
        Assert.Equal(0, profile.ConsecutiveFailures);
    }

    [Fact]
    public void First_outcome_always_updates_trust_even_though_it_is_the_only_attempt()
    {
        IdentityRiskProfileService service = CreateService();
        service.RecordOutcome("user:a", passed: true, observedScore: 0.9);

        IdentityRiskProfile profile = service.GetProfile("user:a");

        // alpha=0.2 (default): 0.5*0.8 + 0.9*0.2 = 0.58
        Assert.Equal(0.58, profile.TrustScore, precision: 6);
        Assert.Equal(1, profile.TotalScans);
        Assert.Equal(1, profile.SuccessfulScans);
        Assert.Equal(0, profile.ConsecutiveFailures);
    }

    [Fact]
    public void Consecutive_failures_increment_and_reset_on_a_pass()
    {
        IdentityRiskProfileService service = CreateService();
        service.RecordOutcome("user:b", passed: false, observedScore: 0.1);
        service.RecordOutcome("user:b", passed: false, observedScore: 0.1);
        Assert.Equal(2, service.GetProfile("user:b").ConsecutiveFailures);

        service.RecordOutcome("user:b", passed: true, observedScore: 0.9);
        Assert.Equal(0, service.GetProfile("user:b").ConsecutiveFailures);
    }

    [Fact]
    public void Rapid_repeated_outcomes_within_the_cooldown_do_not_move_the_smoothed_trust_score()
    {
        // "We do not always update the value": a burst of attempts inside
        // MinProfileUpdateIntervalSeconds should only move trust once (the first update), even
        // though every attempt still counts toward TotalScans/ConsecutiveFailures.
        IdentityRiskProfileService service = CreateService();

        service.RecordOutcome("user:c", passed: true, observedScore: 0.9);
        double afterFirst = service.GetProfile("user:c").TrustScore;

        for (int i = 0; i < 10; i++)
            service.RecordOutcome("user:c", passed: true, observedScore: 1.0);

        IdentityRiskProfile profile = service.GetProfile("user:c");
        Assert.Equal(afterFirst, profile.TrustScore, precision: 9);
        Assert.Equal(11, profile.TotalScans);
    }

    [Fact]
    public void Trust_updates_again_once_the_cooldown_has_elapsed()
    {
        IdentityRiskProfileService service = CreateService(minUpdateIntervalSeconds: 0);

        service.RecordOutcome("user:d", passed: true, observedScore: 0.9);
        double afterFirst = service.GetProfile("user:d").TrustScore;

        service.RecordOutcome("user:d", passed: true, observedScore: 0.9);
        double afterSecond = service.GetProfile("user:d").TrustScore;

        // With zero cooldown, the second update also applies, moving trust further toward 0.9.
        Assert.True(afterSecond > afterFirst);
    }

    [Fact]
    public void Different_identities_have_independent_profiles()
    {
        IdentityRiskProfileService service = CreateService();
        service.RecordOutcome("user:e", passed: false, observedScore: 0.0);

        IdentityRiskProfile untouched = service.GetProfile("user:f");
        Assert.Equal(0.5, untouched.TrustScore);
        Assert.Equal(0, untouched.TotalScans);
    }

    [Fact]
    public void Profiles_default_to_a_retention_tied_to_the_trust_decay_half_life()
    {
        RiskOptions options = new();

        // 7 days at a 72h half-life is ~2.3 half-lives, where a profile still carries about a
        // fifth of its deviation from neutral. The previous 30 days was ten half-lives — memory
        // held for a value arithmetically indistinguishable from a fresh profile.
        Assert.Equal(7, options.ProfileRetentionDays);
        Assert.Equal(72, options.TrustDecayHalfLifeHours);
    }

    [Fact]
    public void A_non_positive_retention_is_floored_rather_than_evicting_on_write()
    {
        // MemoryCacheEntryOptions rejects a zero or negative expiration outright, so an
        // unguarded value here would throw on the first write rather than merely shortening
        // retention. The floor keeps a misconfigured value harmless.
        IdentityRiskProfileService service = CreateService(retentionDays: 0);

        service.RecordOutcome("user:g", passed: true, observedScore: 0.9);
        IdentityRiskProfile profile = service.GetProfile("user:g");

        Assert.Equal(1, profile.TotalScans);
        Assert.Equal(0.58, profile.TrustScore, precision: 6);
    }

    [Fact]
    public void A_configured_retention_still_keeps_the_profile_within_its_window()
    {
        IdentityRiskProfileService service = CreateService(retentionDays: 1);

        service.RecordOutcome("user:h", passed: false, observedScore: 0.1);
        IdentityRiskProfile profile = service.GetProfile("user:h");

        Assert.Equal(1, profile.TotalScans);
        Assert.Equal(1, profile.ConsecutiveFailures);
    }

    private static IdentityRiskProfileService CreateService(
        int minUpdateIntervalSeconds = 5,
        int retentionDays = 7)
    {
        RiskOptions options = new()
        {
            MinProfileUpdateIntervalSeconds = minUpdateIntervalSeconds,
            ProfileRetentionDays = retentionDays,
        };
        return new IdentityRiskProfileService(new MemoryCache(new MemoryCacheOptions()), Options.Create(options));
    }
}
