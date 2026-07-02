using HomeworkCentral.Api.Captcha;

namespace HomeworkCentral.Api.Risk;

/// <summary><see cref="Score"/> is this attempt's trust score (0..1, higher = more human — see
/// <c>HomeworkCentral.Api.Captcha.FCaptcha.IFCaptchaVerifier</c>); <see cref="RequiredScore"/> is
/// the dynamic bar it had to clear, after every adjustment; <see cref="Reasons"/> explains which
/// signals moved the bar, for logging.</summary>
public sealed record RiskAssessment(double Score, double RequiredScore, bool Passed, IReadOnlyList<string> Reasons);

/// <summary>
/// Combines an already-computed trust score with signals that span beyond a single attempt — IP
/// consistency, this identity's captcha track record, and its current request-pattern suspicion
/// from <c>IScrapingDetectionService</c> — into a single pass/fail decision against a threshold
/// that moves per identity and per attempt, instead of one fixed constant for everyone. A normal,
/// well-behaved identity's threshold sits at or below its likely score and it passes silently; a
/// suspicious one's threshold climbs, so the same trust score stops being enough.
/// </summary>
public interface IRiskEngine
{
    RiskAssessment Evaluate(CaptchaAction action, string identity, bool ipMatched, double score);

    /// <summary>Feeds this attempt's outcome back into the identity's trust history for future
    /// attempts. Call once per validated attempt, after <see cref="Evaluate"/>.</summary>
    void RecordOutcome(string identity, RiskAssessment assessment);
}
