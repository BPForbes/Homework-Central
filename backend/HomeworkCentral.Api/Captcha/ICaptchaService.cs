using HomeworkCentral.Api.Captcha.ArrowMatch;
using HomeworkCentral.Api.Captcha.Maze;

namespace HomeworkCentral.Api.Captcha;

/// <summary>Which flow a captcha is gating — used to pick a base risk threshold (see
/// <c>HomeworkCentral.Api.Risk.RiskOptions</c>); a role-granting action is held to a stricter
/// standard than initial signup.</summary>
public enum CaptchaAction
{
    Register,
    VerifyRole,
}

/// <summary>
/// <paramref name="Label"/> is plain instructional text, safe to select/copy.
/// <paramref name="FCaptchaSiteKey"/>/<paramref name="FCaptchaPublicUrl"/> (see
/// <c>HomeworkCentral.Api.Captcha.FCaptcha</c>) are always populated — the "I'm not a robot"
/// checkbox is a mandatory baseline on every challenge, not one of several alternatives. On top of
/// that, exactly one of <paramref name="Content"/> (text — see
/// <c>HomeworkCentral.Api.Captcha.Text</c>), <paramref name="Maze"/> (see
/// <c>HomeworkCentral.Api.Captcha.Maze</c>), or <paramref name="TileRotate"/> (see
/// <c>HomeworkCentral.Api.Captcha.ArrowMatch</c>) is populated, selected by
/// <paramref name="Type"/> (<c>"text"</c> | <c>"maze"</c> | <c>"tileRotate"</c>) — this puzzle is
/// only actually required if FCaptcha's own verdict isn't confident enough on its own; see
/// <see cref="ICaptchaService.ValidateAsync"/>. <paramref name="Content"/> is rendered distorted
/// and non-selectable by the frontend so it can't just be lifted with Ctrl+C.
/// </summary>
public sealed record CaptchaChallengeDto(
    string ChallengeId,
    string Type,
    string Label,
    string? Content,
    MazeDto? Maze,
    TileRotateDto? TileRotate,
    string FCaptchaSiteKey,
    string FCaptchaPublicUrl);

/// <summary>
/// Issues short-lived, single-use captcha challenges and validates submissions. Every challenge
/// carries a mandatory FCaptcha "I'm not a robot" check plus one randomly-picked in-house puzzle
/// (text, maze, or arrow-match — each its own module under <c>HomeworkCentral.Api.Captcha.*</c>).
/// Used both by signup (to grant <c>VerifiedUser</c> instead of <c>Guest</c>) and by the dashboard
/// "Verify" button (to promote an existing Guest).
/// </summary>
/// <summary>Result of checking an FCaptcha widget token without consuming a challenge. The frontend
/// uses this to decide whether to reveal the fallback puzzle or let the user submit with FCaptcha
/// alone.</summary>
public sealed record FCaptchaAssessmentDto(bool Valid, bool PuzzleRequired);

public interface ICaptchaService
{
    CaptchaChallengeDto CreateChallenge();

    /// <summary>
    /// Checks an FCaptcha token and reports whether the fallback puzzle must also be shown/solved.
    /// Does not consume a challenge.
    /// </summary>
    Task<FCaptchaAssessmentDto> AssessFCaptchaAsync(string? token);

    /// <summary>
    /// Validates and consumes a challenge (each challenge can only be checked once, regardless of
    /// outcome, so a captured answer cannot be replayed). FCaptcha verification is mandatory and
    /// checked first: a confidently-human verdict passes on its own; anything less confident falls
    /// back to also requiring the in-house puzzle to be solved correctly, combined with FCaptcha's
    /// trust score through <c>HomeworkCentral.Api.Risk.IRiskEngine</c>'s dynamic threshold.
    /// <paramref name="action"/> selects which base threshold applies in that fallback case.
    /// </summary>
    Task<bool> ValidateAsync(CaptchaSubmissionDto? submission, CaptchaAction action);
}
