using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.Captcha;

/// <summary>Client's answer to a challenge. Exactly one of <see cref="Answer"/>,
/// <see cref="MazePath"/>/<see cref="MazeUnsolvableClaim"/>, or <see cref="TileRotationClicks"/> is
/// relevant, matching the challenge's <c>Type</c> — except <see cref="FCaptchaToken"/>, which is
/// required on every submission regardless of puzzle type; see <c>HomeworkCentral.Api.Captcha.CaptchaService</c>
/// for how the two combine.</summary>
public sealed class CaptchaSubmissionDto
{
    public string ChallengeId { get; set; } = null!;

    /// <summary>The hCaptcha-style "I'm not a robot" token from the FCaptcha widget (see
    /// <c>HomeworkCentral.Api.Captcha.FCaptcha</c>). Mandatory on every submission — this is the
    /// baseline check; the puzzle-specific fields below are only consulted when FCaptcha's own
    /// verdict isn't confident enough on its own.</summary>
    public string? FCaptchaToken { get; set; }

    /// <summary>Text challenges: the retyped code or solved expression.</summary>
    [MaxLength(64)]
    public string? Answer { get; set; }

    /// <summary>Maze challenges: cell indices visited in order, starting at the maze's start cell.
    /// Ignored when <see cref="MazeUnsolvableClaim"/> is set.</summary>
    [MaxLength(121)]
    public List<int>? MazePath { get; set; }

    /// <summary>Maze challenges: true when the player asserts there is no path from A to B instead
    /// of tracing one — some maze challenges are deliberately built as two disconnected regions,
    /// and correctly recognizing that is itself the solve. Takes precedence over
    /// <see cref="MazePath"/> when set.</summary>
    public bool MazeUnsolvableClaim { get; set; }

    /// <summary>Tile-rotate challenges: number of clicks applied to each tile, same order as issued.</summary>
    [MaxLength(9)]
    public List<int>? TileRotationClicks { get; set; }
}
