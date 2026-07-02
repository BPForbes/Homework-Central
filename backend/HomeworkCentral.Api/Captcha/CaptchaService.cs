using HomeworkCentral.Api.Captcha.ArrowMatch;
using HomeworkCentral.Api.Captcha.Maze;
using HomeworkCentral.Api.Captcha.Text;
using HomeworkCentral.Api.Risk;
using HomeworkCentral.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Thin coordinator over three independent puzzle modules — <see cref="TextChallenge"/>,
/// <see cref="MazeGenerator"/>, and <see cref="TileRotatePuzzleGenerator"/>, each in its own
/// namespace and owning its own generation and validation logic. This class only picks a random
/// puzzle type, caches/looks up the server-side answer, and (once the puzzle module confirms the
/// answer itself is correct — a hard requirement, no score substitutes for it) asks
/// <see cref="IRiskEngine"/> to judge the submission's behavioral telemetry, IP consistency, and
/// identity track record against a dynamically computed threshold.
/// </summary>
public sealed class CaptchaService(
    IMemoryCache cache,
    IRiskEngine riskEngine,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CaptchaService> logger) : ICaptchaService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly int[] MazeSizes = [7, 9, 11];

    public CaptchaChallengeDto CreateChallenge()
    {
        string challengeId = Guid.NewGuid().ToString("N");
        string? issuerIp = ResolveClientIp();

        return Random.Shared.Next(3) switch
        {
            0 => CreateTextChallenge(challengeId, issuerIp),
            1 => CreateMazeChallenge(challengeId, issuerIp),
            _ => CreateArrowMatchChallenge(challengeId, issuerIp),
        };
    }

    public bool Validate(CaptchaSubmissionDto? submission, CaptchaAction action)
    {
        if (submission is null || string.IsNullOrWhiteSpace(submission.ChallengeId))
            return false;

        string cacheKey = CacheKey(submission.ChallengeId);
        if (!cache.TryGetValue(cacheKey, out ChallengeRecord? record) || record is null)
            return false;

        cache.Remove(cacheKey);

        // A challenge solved from a different IP than the one it was issued to is a signal it may
        // have been farmed out to a separate solver — but not, on its own, proof of that (mobile
        // networks reassign IPs mid-session). It feeds into the risk engine as a threshold
        // adjustment rather than an automatic reject; see IRiskEngine.
        bool ipMatched = record.IssuerIp is null || string.Equals(record.IssuerIp, ResolveClientIp(), StringComparison.Ordinal);

        bool solved = record.Type switch
        {
            TextChallenge.TypeName => TextChallenge.Validate(record.TextAnswer, submission.Answer),
            MazeGenerator.TypeName => MazeGenerator.Validate(record.Maze!, submission.MazePath, submission.MazeUnsolvableClaim),
            TileRotatePuzzleGenerator.TypeName => TileRotatePuzzleGenerator.Validate(record.TileRotate!, submission.TileRotationClicks),
            _ => false,
        };

        if (!solved)
        {
            logger.LogInformation("Captcha {ChallengeId} rejected: puzzle not solved correctly.", submission.ChallengeId);
            return false;
        }

        string identity = RequestIdentity.Resolve(httpContextAccessor.HttpContext);
        RiskAssessment assessment = riskEngine.Evaluate(action, identity, ipMatched, submission.Behavior);
        riskEngine.RecordOutcome(identity, assessment);

        if (!assessment.Passed)
        {
            logger.LogInformation(
                "Captcha {ChallengeId} risk-denied for {Identity}: score {Score:F2} < required {Required:F2} ({Reasons})",
                submission.ChallengeId,
                identity,
                assessment.Score,
                assessment.RequiredScore,
                string.Join("; ", assessment.Reasons));
        }

        return assessment.Passed;
    }

    private CaptchaChallengeDto CreateTextChallenge(string challengeId, string? issuerIp)
    {
        (string label, string content, string answer) = TextChallenge.Generate();

        Store(challengeId, ChallengeRecord.ForText(answer, issuerIp));
        return new CaptchaChallengeDto(challengeId, TextChallenge.TypeName, label, content, null, null);
    }

    private CaptchaChallengeDto CreateMazeChallenge(string challengeId, string? issuerIp)
    {
        int size = MazeSizes[Random.Shared.Next(MazeSizes.Length)];
        MazeDto maze = MazeGenerator.Generate(size, size);

        Store(challengeId, ChallengeRecord.ForMaze(maze, issuerIp));
        return new CaptchaChallengeDto(
            challengeId,
            MazeGenerator.TypeName,
            "Guide the marker from A to B — or, if there's truly no way through, say so.",
            null,
            maze,
            null);
    }

    private CaptchaChallengeDto CreateArrowMatchChallenge(string challengeId, string? issuerIp)
    {
        TileRotateDto tileRotate = TileRotatePuzzleGenerator.Generate();

        Store(challengeId, ChallengeRecord.ForTileRotate(tileRotate, issuerIp));
        return new CaptchaChallengeDto(
            challengeId,
            TileRotatePuzzleGenerator.TypeName,
            "Rotate each arrow to match its faint target.",
            null,
            null,
            tileRotate);
    }

    private void Store(string challengeId, ChallengeRecord record) =>
        cache.Set(CacheKey(challengeId), record, ChallengeLifetime);

    private string? ResolveClientIp() => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static string CacheKey(string challengeId) => $"captcha:{challengeId}";

    /// <summary>Server-side-only validation context cached per challenge; never sent to the client.</summary>
    private sealed record ChallengeRecord(string Type, string? TextAnswer, MazeDto? Maze, TileRotateDto? TileRotate, string? IssuerIp)
    {
        public static ChallengeRecord ForText(string answer, string? issuerIp) =>
            new(TextChallenge.TypeName, answer, null, null, issuerIp);
        public static ChallengeRecord ForMaze(MazeDto maze, string? issuerIp) =>
            new(MazeGenerator.TypeName, null, maze, null, issuerIp);
        public static ChallengeRecord ForTileRotate(TileRotateDto tileRotate, string? issuerIp) =>
            new(TileRotatePuzzleGenerator.TypeName, null, null, tileRotate, issuerIp);
    }
}
