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
/// <paramref name="Content"/> (text challenges), <paramref name="Maze"/>, or
/// <paramref name="TileRotate"/> is populated, selected by <paramref name="Type"/>
/// (<c>"text"</c> | <c>"maze"</c> | <c>"tileRotate"</c>). <paramref name="Content"/> is rendered
/// distorted and non-selectable by the frontend so it can't just be lifted with Ctrl+C.
/// </summary>
public sealed record CaptchaChallengeDto(
    string ChallengeId,
    string Type,
    string Label,
    string? Content,
    MazeDto? Maze,
    TileRotateDto? TileRotate);

/// <summary>
/// A perfect maze (exactly one simple path between any two cells) on a <paramref name="Width"/> x
/// <paramref name="Height"/> grid, row-major cell indices. Each entry in <paramref name="CellWalls"/>
/// is a bitmask of open passages out of that cell: 1=North, 2=East, 4=South, 8=West.
/// </summary>
public sealed record MazeDto(int Width, int Height, int[] CellWalls, int StartIndex, int EndIndex);

/// <summary>A row of tiles, each rotated a random non-zero number of 90° steps out of alignment;
/// solving means rotating every tile back to 0 (a multiple of 4 steps).</summary>
public sealed record TileRotateDto(TileDto[] Tiles);

public sealed record TileDto(int InitialRotationSteps);

/// <summary>
/// Issues short-lived, single-use captcha challenges (text, maze, or tile-rotate puzzles) and
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
