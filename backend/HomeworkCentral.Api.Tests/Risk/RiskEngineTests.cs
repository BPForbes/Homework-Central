using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Risk;
using HomeworkCentral.Api.ScrapingDetection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeworkCentral.Api.Tests.Risk;

public class RiskEngineTests
{
    [Fact]
    public void First_attempt_from_a_brand_new_identity_adds_the_new_identity_penalty()
    {
        (RiskEngine engine, _) = CreateEngine(behaviorScore: 0.8);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: true, behavior: null);

        // base 0.75 + new-identity 0.05
        Assert.Equal(0.80, assessment.RequiredScore, precision: 6);
        Assert.True(assessment.Passed);
    }

    [Fact]
    public void Ip_mismatch_raises_the_required_score_but_does_not_reject_outright()
    {
        (RiskEngine engine, _) = CreateEngine(behaviorScore: 0.99);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: false, behavior: null);

        // base 0.75 + mismatch 0.20 + new-identity 0.05 = 1.00, clamped to the 0.95 ceiling
        Assert.Equal(0.95, assessment.RequiredScore, precision: 6);
        Assert.True(assessment.Passed); // 0.99 still clears it
        Assert.Contains(assessment.Reasons, r => r.Contains("different IP"));
    }

    [Fact]
    public void Elevated_scraping_suspicion_raises_the_required_score_proportionally()
    {
        ScrapingAssessment scraping = new(0.4, false, "test signal");
        (RiskEngine engine, _) = CreateEngine(behaviorScore: 0.8, scraping);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: true, behavior: null);

        // base 0.75 + new-identity 0.05 + (0.4 * 0.25 weight) = 0.90
        Assert.Equal(0.90, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Consecutive_failures_raise_the_required_score_up_to_the_cap()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine(behaviorScore: 0.8);

        for (int i = 0; i < 5; i++)
            profiles.RecordOutcome("user:repeat", passed: false, observedScore: 0.1);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:repeat", ipMatched: true, behavior: null);

        // base 0.75 + consecutive-failure penalty capped at 0.20 (5 failures * 0.05 would be 0.25
        // uncapped); not a new identity anymore, so no new-identity penalty.
        Assert.Equal(0.95, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Positive_trust_history_lowers_the_required_score()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine(behaviorScore: 0.8);
        profiles.RecordOutcome("user:trusted", passed: true, observedScore: 0.95);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:trusted", ipMatched: true, behavior: null);

        // trust after one update: 0.5*0.8 + 0.95*0.2 = 0.59; bonus = min(0.15, (0.59-0.5)*2*0.15) = 0.027
        // required = base 0.75 (not a new identity: TotalScans=1) - 0.027 = 0.723
        Assert.Equal(0.723, assessment.RequiredScore, precision: 3);
    }

    [Fact]
    public void Required_score_never_drops_below_the_configured_floor()
    {
        RiskOptions options = new()
        {
            RegisterBaseThreshold = 0.30,
            VerifyRoleBaseThreshold = 0.30,
            NewIdentityPenalty = 0,
            MaxTrustBonus = 0.5,
            MinRequiredScore = 0.35,
        };
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine(behaviorScore: 0.5, options: options);
        profiles.RecordOutcome("user:verytrusted", passed: true, observedScore: 1.0);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:verytrusted", ipMatched: true, behavior: null);

        Assert.Equal(0.35, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Register_action_uses_a_lower_base_threshold_than_verify_role()
    {
        (RiskEngine engine, _) = CreateEngine(behaviorScore: 0.72);

        RiskAssessment registerAssessment = engine.Evaluate(CaptchaAction.Register, "user:a", ipMatched: true, behavior: null);
        RiskAssessment verifyAssessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:b", ipMatched: true, behavior: null);

        Assert.True(registerAssessment.RequiredScore < verifyAssessment.RequiredScore);
    }

    [Fact]
    public void RecordOutcome_feeds_the_assessment_back_into_the_identity_profile()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine(behaviorScore: 0.9);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:c", ipMatched: true, behavior: null);
        engine.RecordOutcome("user:c", assessment);

        IdentityRiskProfile profile = profiles.GetProfile("user:c");
        Assert.Equal(1, profile.TotalScans);
        Assert.Equal(1, profile.SuccessfulScans);
    }

    private static (RiskEngine Engine, IIdentityRiskProfileService Profiles) CreateEngine(
        double behaviorScore,
        ScrapingAssessment? scraping = null,
        RiskOptions? options = null)
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        IOptions<RiskOptions> wrappedOptions = Options.Create(options ?? new RiskOptions());
        IIdentityRiskProfileService profiles = new IdentityRiskProfileService(cache, wrappedOptions);
        IScrapingDetectionService scrapingService = new FakeScrapingDetectionService(
            scraping ?? new ScrapingAssessment(0, false, null));

        RiskEngine engine = new(new FakeBehaviorScoringService(behaviorScore), profiles, scrapingService, wrappedOptions);
        return (engine, profiles);
    }

    private sealed class FakeBehaviorScoringService(double score) : IBehaviorScoringService
    {
        public double ComputeScore(CaptchaBehaviorDto? telemetry) => score;
    }

    private sealed class FakeScrapingDetectionService(ScrapingAssessment assessment) : IScrapingDetectionService
    {
        public ScrapingAssessment RecordRequest(ScrapingRequestSample sample) => assessment;
        public ScrapingAssessment PeekAssessment(string identity) => assessment;
    }
}
