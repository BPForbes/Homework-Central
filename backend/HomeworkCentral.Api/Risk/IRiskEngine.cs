using HomeworkCentral.Api.Captcha;

namespace HomeworkCentral.Api.Risk;

/// <summary><see cref="Score"/> is this attempt's observed behavioral score (0..1);
/// <see cref="RequiredScore"/> is the dynamic bar it had to clear, after every adjustment;
/// <see cref="Reasons"/> explains which signals moved the bar, for logging.</summary>
public sealed record RiskAssessment(double Score, double RequiredScore, bool Passed, IReadOnlyList<string> Reasons);

/// <summary>
/// Combines this attempt's behavioral telemetry with signals that span beyond a single attempt —
/// IP consistency, this identity's captcha track record, and its current request-pattern
/// suspicion from <c>IScrapingDetectionService</c> — into a single pass/fail decision against a
/// threshold that moves per identity and per attempt, instead of one fixed constant for everyone.
/// A normal, well-behaved identity's threshold sits at or below its likely score and it passes
/// silently; a suspicious one's threshold climbs, so the same puzzle and the same "pretty good"
/// mouse movement stop being enough.
/// </summary>
public interface IRiskEngine
{
    RiskAssessment Evaluate(CaptchaAction action, string identity, bool ipMatched, CaptchaBehaviorDto? behavior);

    /// <summary>Feeds this attempt's outcome back into the identity's trust history for future
    /// attempts. Call once per validated attempt, after <see cref="Evaluate"/>.</summary>
    void RecordOutcome(string identity, RiskAssessment assessment);
}
