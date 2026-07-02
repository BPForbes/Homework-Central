namespace HomeworkCentral.Api.Captcha;

/// <summary>Client's answer to a challenge plus the behavioral telemetry collected while solving
/// it. Exactly one of <see cref="Answer"/>, <see cref="MazePath"/>/<see cref="MazeUnsolvableClaim"/>,
/// or <see cref="TileRotationClicks"/> is relevant, matching the challenge's <c>Type</c>.</summary>
public sealed class CaptchaSubmissionDto
{
    public string ChallengeId { get; set; } = null!;

    /// <summary>Text challenges: the retyped code or solved expression.</summary>
    public string? Answer { get; set; }

    /// <summary>Maze challenges: cell indices visited in order, starting at the maze's start cell.
    /// Ignored when <see cref="MazeUnsolvableClaim"/> is set.</summary>
    public List<int>? MazePath { get; set; }

    /// <summary>Maze challenges: true when the player asserts there is no path from A to B instead
    /// of tracing one — some maze challenges are deliberately built as two disconnected regions,
    /// and correctly recognizing that is itself the solve. Takes precedence over
    /// <see cref="MazePath"/> when set.</summary>
    public bool MazeUnsolvableClaim { get; set; }

    /// <summary>Tile-rotate challenges: number of clicks applied to each tile, same order as issued.</summary>
    public List<int>? TileRotationClicks { get; set; }

    public CaptchaBehaviorDto? Behavior { get; set; }
}

/// <summary>Raw behavioral telemetry collected client-side while a challenge was on screen. Scored
/// server-side by <see cref="IBehaviorScoringService"/> — the client never computes or reports a
/// score itself, since a client-computed score could simply be fabricated by a bot.</summary>
public sealed class CaptchaBehaviorDto
{
    public List<MouseSampleDto>? MouseSamples { get; set; }

    /// <summary>Milliseconds between consecutive keydown events on the answer field, if any.</summary>
    public List<int>? KeyIntervalsMs { get; set; }

    /// <summary>Total time in milliseconds from when the challenge was shown to when it was submitted.</summary>
    public int TotalDurationMs { get; set; }

    /// <summary>Client-reported <c>navigator.webdriver</c> — a cheap but easily-spoofed signal, so
    /// it's only one weighted factor among several, never a sole pass/fail gate.</summary>
    public bool WebdriverFlag { get; set; }

    /// <summary>Count of discrete interactions (clicks/drags/rotations) with the puzzle itself.</summary>
    public int InteractionCount { get; set; }
}

public sealed class MouseSampleDto
{
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Milliseconds since the challenge was shown.</summary>
    public int TMs { get; set; }
}
