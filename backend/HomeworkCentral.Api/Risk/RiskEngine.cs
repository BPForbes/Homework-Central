using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.ScrapingDetection;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Risk;

public sealed class RiskEngine(
    IBehaviorScoringService behaviorScoring,
    IIdentityRiskProfileService profiles,
    IScrapingDetectionService scrapingDetection,
    IOptions<RiskOptions> options) : IRiskEngine
{
    public RiskAssessment Evaluate(CaptchaAction action, string identity, bool ipMatched, CaptchaBehaviorDto? behavior)
    {
        RiskOptions opts = options.Value;
        double score = behaviorScoring.ComputeScore(behavior);
        IdentityRiskProfile profile = profiles.GetProfile(identity);
        ScrapingAssessment scraping = scrapingDetection.PeekAssessment(identity);

        double required = action == CaptchaAction.Register ? opts.RegisterBaseThreshold : opts.VerifyRoleBaseThreshold;
        List<string> reasons = [];

        if (!ipMatched)
        {
            required += opts.IpMismatchPenalty;
            reasons.Add("submitted from a different IP than the challenge was issued to");
        }

        if (profile.TotalScans == 0)
        {
            required += opts.NewIdentityPenalty;
            reasons.Add("first captcha attempt from this identity");
        }

        if (profile.ConsecutiveFailures > 0)
        {
            double penalty = Math.Min(opts.MaxConsecutiveFailurePenalty, profile.ConsecutiveFailures * opts.ConsecutiveFailurePenaltyPerFailure);
            required += penalty;
            reasons.Add($"{profile.ConsecutiveFailures} recent consecutive failures from this identity");
        }

        if (scraping.SuspicionScore > 0)
        {
            required += scraping.SuspicionScore * opts.ScrapingSuspicionWeight;
            reasons.Add($"elevated request-pattern suspicion ({scraping.SuspicionScore:F2})");
        }

        if (profile.TrustScore > 0.5)
        {
            double bonus = Math.Min(opts.MaxTrustBonus, (profile.TrustScore - 0.5) * 2 * opts.MaxTrustBonus);
            required -= bonus;
            reasons.Add($"positive trust history ({profile.TrustScore:F2})");
        }

        required = Math.Clamp(required, opts.MinRequiredScore, opts.MaxRequiredScore);
        bool passed = score >= required;

        return new RiskAssessment(score, required, passed, reasons);
    }

    public void RecordOutcome(string identity, RiskAssessment assessment) =>
        profiles.RecordOutcome(identity, assessment.Passed, assessment.Score);
}
