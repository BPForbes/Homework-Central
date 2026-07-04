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
        (RiskEngine engine, _) = CreateEngine();

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: true, score: 0.6);

        // base 0.50 + new-identity 0.05
        Assert.Equal(0.55, assessment.RequiredScore, precision: 6);
        Assert.True(assessment.Passed);
    }

    [Fact]
    public void Ip_mismatch_raises_the_required_score_but_does_not_reject_outright()
    {
        (RiskEngine engine, _) = CreateEngine();

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: false, score: 0.68);

        // base 0.50 + mismatch 0.15 + new-identity 0.05 = 0.70, clamped to the 0.68 ceiling
        Assert.Equal(0.68, assessment.RequiredScore, precision: 6);
        Assert.True(assessment.Passed); // 0.68 still clears it (score == required)
        Assert.Contains(assessment.Reasons, r => r.Contains("different IP"));
    }

    [Fact]
    public void Elevated_scraping_suspicion_raises_the_required_score_proportionally()
    {
        ScrapingAssessment scraping = new(0.4, false, "test signal");
        (RiskEngine engine, _) = CreateEngine(scraping);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:fresh", ipMatched: true, score: 0.6);

        // base 0.50 + new-identity 0.05 + (0.4 * 0.20 weight) = 0.63
        Assert.Equal(0.63, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Consecutive_failures_raise_the_required_score_up_to_the_cap()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine();

        for (int i = 0; i < 5; i++)
            profiles.RecordOutcome("user:repeat", passed: false, observedScore: 0.1);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:repeat", ipMatched: true, score: 0.6);

        // base 0.50 + consecutive-failure penalty capped at 0.15 (5 failures * 0.05 would be 0.25
        // uncapped); not a new identity anymore, so no new-identity penalty.
        Assert.Equal(0.65, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Positive_trust_history_lowers_the_required_score()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine();
        profiles.RecordOutcome("user:trusted", passed: true, observedScore: 0.95);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:trusted", ipMatched: true, score: 0.6);

        // trust after one update: 0.5*0.8 + 0.95*0.2 = 0.59; bonus = min(0.10, (0.59-0.5)*2*0.10) = 0.018
        // required = base 0.50 (not a new identity: TotalScans=1) - 0.018 = 0.482
        Assert.Equal(0.482, assessment.RequiredScore, precision: 3);
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
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine(options: options);
        profiles.RecordOutcome("user:verytrusted", passed: true, observedScore: 1.0);

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:verytrusted", ipMatched: true, score: 0.5);

        Assert.Equal(0.35, assessment.RequiredScore, precision: 6);
    }

    [Fact]
    public void Register_action_uses_a_lower_base_threshold_than_verify_role()
    {
        (RiskEngine engine, _) = CreateEngine();

        RiskAssessment registerAssessment = engine.Evaluate(CaptchaAction.Register, "user:a", ipMatched: true, score: 0.6);
        RiskAssessment verifyAssessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:b", ipMatched: true, score: 0.6);

        Assert.True(registerAssessment.RequiredScore < verifyAssessment.RequiredScore);
    }

    [Fact]
    public void RecordOutcome_feeds_the_assessment_back_into_the_identity_profile()
    {
        (RiskEngine engine, IIdentityRiskProfileService profiles) = CreateEngine();

        RiskAssessment assessment = engine.Evaluate(CaptchaAction.VerifyRole, "user:c", ipMatched: true, score: 0.6);
        engine.RecordOutcome("user:c", assessment);

        IdentityRiskProfile profile = profiles.GetProfile("user:c");
        Assert.Equal(1, profile.TotalScans);
        Assert.Equal(1, profile.SuccessfulScans);
    }

    private static (RiskEngine Engine, IIdentityRiskProfileService Profiles) CreateEngine(
        ScrapingAssessment? scraping = null,
        RiskOptions? options = null)
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        IOptions<RiskOptions> wrappedOptions = Options.Create(options ?? new RiskOptions());
        IIdentityRiskProfileService profiles = new IdentityRiskProfileService(cache, wrappedOptions);
        IScrapingDetectionService scrapingService = new FakeScrapingDetectionService(
            scraping ?? new ScrapingAssessment(0, false, null));

        RiskEngine engine = new(profiles, scrapingService, wrappedOptions);
        return (engine, profiles);
    }

    private sealed class FakeScrapingDetectionService(ScrapingAssessment assessment) : IScrapingDetectionService
    {
        public ScrapingAssessment RecordRequest(ScrapingRequestSample sample) => assessment;
        public ScrapingAssessment PeekAssessment(string identity) => assessment;
    }
}
