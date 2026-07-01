using Microsoft.Extensions.Caching.Memory;

namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Randomly issues one of three challenge kinds — retype-a-code / solve-an-expression (text), a
/// generated maze, or a tile-rotation puzzle — and validates submissions against two independent
/// gates: the puzzle must actually be solved correctly, AND the behavioral telemetry submitted
/// alongside it must score at or above <see cref="PassingScore"/> from <see cref="IBehaviorScoringService"/>.
/// Both gates are required; solving the puzzle alone is not enough.
/// </summary>
public sealed class CaptchaService(IMemoryCache cache, IBehaviorScoringService behaviorScoring) : ICaptchaService
{
    private const double PassingScore = 0.75;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    // Excludes visually ambiguous characters (0/O, 1/I/L) since the code is typed back by hand.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int MazeWidth = 6;
    private const int MazeHeight = 6;
    private const int MaxMazePathLength = MazeWidth * MazeHeight * 4;

    public CaptchaChallengeDto CreateChallenge()
    {
        string challengeId = Guid.NewGuid().ToString("N");

        return Random.Shared.Next(3) switch
        {
            0 => CreateTextChallenge(challengeId),
            1 => CreateMazeChallenge(challengeId),
            _ => CreateTileRotateChallenge(challengeId),
        };
    }

    public bool Validate(CaptchaSubmissionDto? submission)
    {
        if (submission is null || string.IsNullOrWhiteSpace(submission.ChallengeId))
            return false;

        string cacheKey = CacheKey(submission.ChallengeId);
        if (!cache.TryGetValue(cacheKey, out ChallengeRecord? record) || record is null)
            return false;

        cache.Remove(cacheKey);

        bool solved = record.Type switch
        {
            CaptchaChallengeTypes.Text => ValidateText(record.TextAnswer, submission.Answer),
            CaptchaChallengeTypes.Maze => ValidateMaze(record.Maze!, submission.MazePath),
            CaptchaChallengeTypes.TileRotate => ValidateTileRotate(record.TileRotate!, submission.TileRotationClicks),
            _ => false,
        };

        if (!solved)
            return false;

        double score = behaviorScoring.ComputeScore(submission.Behavior);
        return score >= PassingScore;
    }

    private CaptchaChallengeDto CreateTextChallenge(string challengeId)
    {
        (string label, string content, string answer) = Random.Shared.Next(2) == 0
            ? BuildArithmeticChallenge()
            : BuildCodeChallenge();

        Store(challengeId, ChallengeRecord.ForText(answer));
        return new CaptchaChallengeDto(challengeId, CaptchaChallengeTypes.Text, label, content, null, null);
    }

    private CaptchaChallengeDto CreateMazeChallenge(string challengeId)
    {
        MazeDto maze = MazeGenerator.Generate(MazeWidth, MazeHeight);
        Store(challengeId, ChallengeRecord.ForMaze(maze));
        return new CaptchaChallengeDto(
            challengeId,
            CaptchaChallengeTypes.Maze,
            "Guide the marker from A to B.",
            null,
            maze,
            null);
    }

    private CaptchaChallengeDto CreateTileRotateChallenge(string challengeId)
    {
        TileRotateDto tileRotate = TileRotatePuzzleGenerator.Generate();
        Store(challengeId, ChallengeRecord.ForTileRotate(tileRotate));
        return new CaptchaChallengeDto(
            challengeId,
            CaptchaChallengeTypes.TileRotate,
            "Rotate every tile until it's aligned.",
            null,
            null,
            tileRotate);
    }

    private void Store(string challengeId, ChallengeRecord record) =>
        cache.Set(CacheKey(challengeId), record, ChallengeLifetime);

    private static bool ValidateText(string? expected, string? answer)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(answer))
            return false;

        return string.Equals(expected.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidateMaze(MazeDto maze, List<int>? path)
    {
        if (path is null || path.Count < 2 || path.Count > MaxMazePathLength)
            return false;

        if (path[0] != maze.StartIndex || path[^1] != maze.EndIndex)
            return false;

        for (int i = 1; i < path.Count; i++)
        {
            if (!AreConnected(maze, path[i - 1], path[i]))
                return false;
        }

        return true;
    }

    private static bool AreConnected(MazeDto maze, int from, int to)
    {
        if (from < 0 || from >= maze.CellWalls.Length || to < 0 || to >= maze.CellWalls.Length)
            return false;

        int fromX = from % maze.Width;
        int fromY = from / maze.Width;
        int toX = to % maze.Width;
        int toY = to / maze.Width;
        int dx = toX - fromX;
        int dy = toY - fromY;

        return (dx, dy) switch
        {
            (0, -1) => (maze.CellWalls[from] & MazeDirections.North) != 0,
            (1, 0) => (maze.CellWalls[from] & MazeDirections.East) != 0,
            (0, 1) => (maze.CellWalls[from] & MazeDirections.South) != 0,
            (-1, 0) => (maze.CellWalls[from] & MazeDirections.West) != 0,
            _ => false,
        };
    }

    private static bool ValidateTileRotate(TileRotateDto tileRotate, List<int>? clicks)
    {
        if (clicks is null || clicks.Count != tileRotate.Tiles.Length)
            return false;

        for (int i = 0; i < clicks.Count; i++)
        {
            // Sanity cap: no legitimate solve needs more than a handful of clicks per tile.
            if (clicks[i] < 0 || clicks[i] > 12)
                return false;

            int finalSteps = (tileRotate.Tiles[i].InitialRotationSteps + clicks[i]) % 4;
            if (finalSteps != 0)
                return false;
        }

        return true;
    }

    private static (string Label, string Content, string Answer) BuildArithmeticChallenge()
    {
        int a = Random.Shared.Next(2, 10);
        int b = Random.Shared.Next(2, 10);
        return ("To prove you're human, solve:", $"{a} + {b}", (a + b).ToString());
    }

    private static (string Label, string Content, string Answer) BuildCodeChallenge()
    {
        char[] code = new char[6];
        for (int i = 0; i < code.Length; i++)
            code[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];

        string codeText = new(code);
        return ("Retype this verification code exactly:", codeText, codeText);
    }

    private static string CacheKey(string challengeId) => $"captcha:{challengeId}";

    /// <summary>Server-side-only validation context cached per challenge; never sent to the client.</summary>
    private sealed record ChallengeRecord(string Type, string? TextAnswer, MazeDto? Maze, TileRotateDto? TileRotate)
    {
        public static ChallengeRecord ForText(string answer) => new(CaptchaChallengeTypes.Text, answer, null, null);
        public static ChallengeRecord ForMaze(MazeDto maze) => new(CaptchaChallengeTypes.Maze, null, maze, null);
        public static ChallengeRecord ForTileRotate(TileRotateDto tileRotate) => new(CaptchaChallengeTypes.TileRotate, null, null, tileRotate);
    }
}
