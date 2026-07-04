namespace HomeworkCentral.Api.Captcha.Maze;

/// <summary>
/// Usually a "perfect maze" (exactly one simple path between <paramref name="StartIndex"/> and
/// <paramref name="EndIndex"/>) on a <paramref name="Width"/> x <paramref name="Height"/> grid,
/// row-major cell indices — but some challenges are deliberately built as two disconnected regions
/// with no path between them at all, and correctly recognizing that is itself a valid solve (see
/// <see cref="HomeworkCentral.Api.Captcha.CaptchaSubmissionDto.MazeUnsolvableClaim"/>). Each entry
/// in <paramref name="CellWalls"/> is a bitmask of open passages out of that cell: 1=North, 2=East,
/// 4=South, 8=West.
/// </summary>
public sealed record MazeDto(int Width, int Height, int[] CellWalls, int StartIndex, int EndIndex);
