namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Scores how human-like a captcha attempt's mouse/keyboard/timing telemetry looks, from 0
/// (certainly automated) to 1 (certainly human). Purely heuristic — not a substitute for a
/// maintained third-party risk engine, but meaningfully raises the bar over "no behavioral check
/// at all" against naive/unsophisticated bots (plain HTTP scripts, default headless browsers).
/// </summary>
public interface IBehaviorScoringService
{
    double ComputeScore(CaptchaBehaviorDto? telemetry);
}
