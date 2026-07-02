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
/// <paramref name="Label"/> is plain instructional text, safe to select/copy. Exactly one of
/// <paramref name="Content"/> (text challenges — see <c>HomeworkCentral.Api.Captcha.Text</c>),
/// <paramref name="Maze"/> (see <c>HomeworkCentral.Api.Captcha.Maze</c>), or
/// <paramref name="TileRotate"/> (see <c>HomeworkCentral.Api.Captcha.ArrowMatch</c>) is populated,
/// selected by <paramref name="Type"/> (<c>"text"</c> | <c>"maze"</c> | <c>"tileRotate"</c>).
/// <paramref name="Content"/> is rendered distorted and non-selectable by the frontend so it can't
/// just be lifted with Ctrl+C.
/// </summary>
public sealed record CaptchaChallengeDto(
    string ChallengeId,
    string Type,
    string Label,
    string? Content,
    MazeDto? Maze,
    TileRotateDto? TileRotate);

/// <summary>
/// Issues short-lived, single-use captcha challenges (text, maze, or arrow-match puzzles — each its
/// own module under <c>HomeworkCentral.Api.Captcha.*</c>, generating and validating itself) and
/// validates submissions. Used both by signup (to grant <c>VerifiedUser</c> instead of
/// <c>Guest</c>) and by the dashboard "Verify" button (to promote an existing Guest).
/// </summary>
public interface ICaptchaService
{
    CaptchaChallengeDto CreateChallenge();

    /// <summary>
    /// Validates and consumes a challenge (each challenge can only be checked once, regardless of
    /// outcome, so a captured answer cannot be replayed) and requires BOTH the puzzle answer to be
    /// correct AND the behavioral risk score computed from <see cref="CaptchaSubmissionDto.Behavior"/>
    /// to clear a threshold that is computed dynamically per attempt — see
    /// <c>HomeworkCentral.Api.Risk.IRiskEngine</c>. <paramref name="action"/> selects which base
    /// threshold applies.
    /// </summary>
    bool Validate(CaptchaSubmissionDto? submission, CaptchaAction action);
}
