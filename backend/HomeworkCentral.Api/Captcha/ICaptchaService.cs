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
/// Usually a "perfect maze" (exactly one simple path between <paramref name="StartIndex"/> and
/// <paramref name="EndIndex"/>) on a <paramref name="Width"/> x <paramref name="Height"/> grid,
/// row-major cell indices — but some challenges are deliberately built as two disconnected regions
/// with no path between them at all, and correctly recognizing that is itself a valid solve (see
/// <see cref="CaptchaSubmissionDto.MazeUnsolvableClaim"/>). Each entry in
/// <paramref name="CellWalls"/> is a bitmask of open passages out of that cell: 1=North, 2=East,
/// 4=South, 8=West.
/// </summary>
public sealed record MazeDto(int Width, int Height, int[] CellWalls, int StartIndex, int EndIndex);

/// <summary>A 3x3 grid of arrow tiles. Each tile has its own random target orientation — solving
/// isn't "rotate back to a fixed direction," it's rotating every tile to match its own
/// <see cref="TileDto.TargetRotationSteps"/>.</summary>
public sealed record TileRotateDto(TileDto[] Tiles);

/// <summary>One arrow tile. Both fields are steps of 45° (0–7, one of 8 compass positions).
/// <paramref name="InitialRotationSteps"/> is where it starts; <paramref name="TargetRotationSteps"/>
/// is where the player must rotate it to — never equal to the initial position, and not fixed to
/// any one direction across tiles or challenges.</summary>
public sealed record TileDto(int InitialRotationSteps, int TargetRotationSteps);

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
