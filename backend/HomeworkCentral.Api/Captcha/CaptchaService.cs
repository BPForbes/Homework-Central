using System.Security.Cryptography;
using System.Text;
using HomeworkCentral.Api.Captcha.ArrowMatch;
using HomeworkCentral.Api.Captcha.FCaptcha;
using HomeworkCentral.Api.Captcha.Maze;
using HomeworkCentral.Api.Captcha.Text;
using HomeworkCentral.Api.Risk;
using HomeworkCentral.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Coordinates a mandatory FCaptcha "I'm not a robot" check (see <see cref="IFCaptchaVerifier"/>)
/// with one randomly-picked in-house puzzle module — <see cref="TextChallenge"/>,
/// <see cref="MazeGenerator"/>, or <see cref="TileRotatePuzzleGenerator"/>, each in its own
/// namespace and owning its own generation and validation logic. FCaptcha is checked first on
/// every attempt; a confidently-human verdict is accepted on its own, and anything less confident
/// falls back to also requiring the puzzle to be solved correctly, with FCaptcha's trust score fed
/// into <see cref="IRiskEngine"/>'s dynamic threshold in place of the raw-telemetry heuristic this
/// class used to compute itself.
/// </summary>
public sealed class CaptchaService(
    IMemoryCache cache,
    IFCaptchaVerifier fCaptchaVerifier,
    IRiskEngine riskEngine,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CaptchaService> logger) : ICaptchaService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenVerificationLifetime = TimeSpan.FromMinutes(10);
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

    public async Task<FCaptchaAssessmentDto> AssessFCaptchaAsync(string? token)
    {
        string identity = RequestIdentity.Resolve(httpContextAccessor.HttpContext);
        FCaptchaVerification verification = await VerifyFCaptchaOnceAsync(token);
        if (!verification.Valid)
        {
            RecordFailure(identity, verification.TrustScore);
            return new FCaptchaAssessmentDto(false, false);
        }

        bool puzzleRequired = verification.TrustScore < fCaptchaVerifier.AllowTrustScore;
        return new FCaptchaAssessmentDto(true, puzzleRequired);
    }

    public async Task<bool> ValidateAsync(CaptchaSubmissionDto? submission, CaptchaAction action)
    {
        if (submission is null || string.IsNullOrWhiteSpace(submission.ChallengeId))
            return false;

        string cacheKey = CacheKey(submission.ChallengeId);
        if (!cache.TryGetValue(cacheKey, out ChallengeRecord? record) || record is null)
            return false;

        if (!record.TryConsume())
            return false;

        cache.Remove(cacheKey);

        string identity = RequestIdentity.Resolve(httpContextAccessor.HttpContext);
        bool ipMatched = record.IssuerIp is null || string.Equals(record.IssuerIp, ResolveClientIp(), StringComparison.Ordinal);

        FCaptchaVerification verification = await VerifyFCaptchaOnceAsync(submission.FCaptchaToken);
        if (!verification.Valid)
        {
            logger.LogInformation("Captcha {ChallengeId} rejected: FCaptcha token did not verify.", submission.ChallengeId);
            RecordFailure(identity, verification.TrustScore);
            return false;
        }

        if (verification.TrustScore >= fCaptchaVerifier.AllowTrustScore)
        {
            RiskAssessment assessment = riskEngine.Evaluate(action, identity, ipMatched, verification.TrustScore);
            riskEngine.RecordOutcome(identity, assessment with { Passed = true, Score = verification.TrustScore });
            return true;
        }

        bool solved = record.Type switch
        {
            TextChallenge.TypeName => TextChallenge.Validate(record.TextAnswer, submission.Answer),
            MazeGenerator.TypeName => MazeGenerator.Validate(record.Maze!, submission.MazePath, submission.MazeUnsolvableClaim),
            TileRotatePuzzleGenerator.TypeName => TileRotatePuzzleGenerator.Validate(record.TileRotate!, submission.TileRotationClicks),
            _ => false,
        };

        if (!solved)
        {
            logger.LogInformation(
                "Captcha {ChallengeId} rejected: FCaptcha trust score {TrustScore:F2} was not confident enough and the fallback puzzle was not solved correctly.",
                submission.ChallengeId,
                verification.TrustScore);
            RecordFailure(identity, verification.TrustScore);
            return false;
        }

        RiskAssessment puzzleAssessment = riskEngine.Evaluate(action, identity, ipMatched, verification.TrustScore);
        riskEngine.RecordOutcome(identity, puzzleAssessment);

        if (!puzzleAssessment.Passed)
        {
            logger.LogInformation(
                "Captcha {ChallengeId} risk-denied for {IdentityHash}: score {Score:F2} < required {Required:F2} ({Reasons})",
                submission.ChallengeId,
                RedactIdentity(identity),
                puzzleAssessment.Score,
                puzzleAssessment.RequiredScore,
                string.Join("; ", puzzleAssessment.Reasons));
        }

        return puzzleAssessment.Passed;
    }

    private async Task<FCaptchaVerification> VerifyFCaptchaOnceAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new FCaptchaVerification(false, 0.0);

        string cacheKey = TokenCacheKey(token);
        if (cache.TryGetValue(cacheKey, out FCaptchaVerification? cached) && cached is not null)
            return cached;

        FCaptchaVerification verification = await fCaptchaVerifier.VerifyAsync(token);
        cache.Set(cacheKey, verification, TokenVerificationLifetime);
        return verification;
    }

    private void RecordFailure(string identity, double trustScore) =>
        riskEngine.RecordOutcome(identity, new RiskAssessment(trustScore, 0, false, []));

    private static string RedactIdentity(string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];

    private CaptchaChallengeDto CreateTextChallenge(string challengeId, string? issuerIp)
    {
        (string label, string content, string answer) = TextChallenge.Generate();

        Store(challengeId, ChallengeRecord.ForText(answer, issuerIp));
        return new CaptchaChallengeDto(
            challengeId, TextChallenge.TypeName, label, content, null, null,
            fCaptchaVerifier.SiteKey, fCaptchaVerifier.PublicUrl);
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
            null,
            fCaptchaVerifier.SiteKey,
            fCaptchaVerifier.PublicUrl);
    }

    private CaptchaChallengeDto CreateArrowMatchChallenge(string challengeId, string? issuerIp)
    {
        TileRotateDto tileRotate = TileRotatePuzzleGenerator.Generate();

        Store(challengeId, ChallengeRecord.ForTileRotate(tileRotate, issuerIp));
        return new CaptchaChallengeDto(
            challengeId,
            TileRotatePuzzleGenerator.TypeName,
            "Rotate each arrow until they are all aligned.",
            null,
            null,
            tileRotate,
            fCaptchaVerifier.SiteKey,
            fCaptchaVerifier.PublicUrl);
    }

    private void Store(string challengeId, ChallengeRecord record) =>
        cache.Set(CacheKey(challengeId), record, ChallengeLifetime);

    private string? ResolveClientIp() => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static string CacheKey(string challengeId) => $"captcha:{challengeId}";

    private static string TokenCacheKey(string token) =>
        $"fcaptcha-verification:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";

    /// <summary>Server-side-only validation context cached per challenge; never sent to the client.</summary>
    private sealed class ChallengeRecord
    {
        private int _consumed;

        public string Type { get; }
        public string? TextAnswer { get; }
        public MazeDto? Maze { get; }
        public TileRotateDto? TileRotate { get; }
        public string? IssuerIp { get; }

        private ChallengeRecord(string type, string? textAnswer, MazeDto? maze, TileRotateDto? tileRotate, string? issuerIp)
        {
            Type = type;
            TextAnswer = textAnswer;
            Maze = maze;
            TileRotate = tileRotate;
            IssuerIp = issuerIp;
        }

        public bool TryConsume() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;

        public static ChallengeRecord ForText(string answer, string? issuerIp) =>
            new(TextChallenge.TypeName, answer, null, null, issuerIp);

        public static ChallengeRecord ForMaze(MazeDto maze, string? issuerIp) =>
            new(MazeGenerator.TypeName, null, maze, null, issuerIp);

        public static ChallengeRecord ForTileRotate(TileRotateDto tileRotate, string? issuerIp) =>
            new(TileRotatePuzzleGenerator.TypeName, null, null, tileRotate, issuerIp);
    }
}
