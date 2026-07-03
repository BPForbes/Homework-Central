using System.Net;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.ArrowMatch;
using HomeworkCentral.Api.Captcha.FCaptcha;
using HomeworkCentral.Api.Captcha.Maze;
using HomeworkCentral.Api.Risk;
using HomeworkCentral.Api.ScrapingDetection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeworkCentral.Api.Tests.Captcha;

public class CaptchaServiceTests
{
    [Fact]
    public void Every_challenge_carries_the_verifiers_site_key_and_public_url()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = service.CreateChallenge();

        Assert.Equal(verifier.SiteKey, challenge.FCaptchaSiteKey);
        Assert.Equal(verifier.PublicUrl, challenge.FCaptchaPublicUrl);
    }

    [Fact]
    public async Task Confident_fcaptcha_verdict_passes_without_solving_the_puzzle()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = service.CreateChallenge();
        verifier.NextResult = new FCaptchaVerification(true, 0.9); // >= AllowTrustScore (0.7)

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            // Deliberately no Answer/MazePath/TileRotationClicks — a confident verdict alone must
            // be sufficient regardless of which puzzle type was issued alongside it.
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Assess_fcaptcha_reports_no_puzzle_when_confident()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        verifier.NextResult = new FCaptchaVerification(true, 0.9);

        FCaptchaAssessmentDto assessment = await service.AssessFCaptchaAsync("token");

        Assert.True(assessment.Valid);
        Assert.False(assessment.PuzzleRequired);
    }

    [Fact]
    public async Task Assess_fcaptcha_reports_puzzle_when_uncertain()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        FCaptchaAssessmentDto assessment = await service.AssessFCaptchaAsync("token");

        Assert.True(assessment.Valid);
        Assert.True(assessment.PuzzleRequired);
    }

    [Fact]
    public async Task Assess_fcaptcha_reports_invalid_without_puzzle()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        verifier.NextResult = new FCaptchaVerification(false, 0.0);

        FCaptchaAssessmentDto assessment = await service.AssessFCaptchaAsync("token");

        Assert.False(assessment.Valid);
        Assert.False(assessment.PuzzleRequired);
    }

    [Fact]
    public async Task Invalid_fcaptcha_token_fails_even_with_a_correct_puzzle_answer()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        verifier.NextResult = new FCaptchaVerification(false, 0.0);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            Answer = CaptchaTestSolvers.SolveText(challenge.Content!),
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Uncertain_verdict_with_correct_answer_passes()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        // Below AllowTrustScore (0.7) -> falls back to the puzzle. Required score for a first
        // attempt is the base 0.50 + new-identity penalty 0.05 = 0.55; 0.6 clears it.
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            Answer = CaptchaTestSolvers.SolveText(challenge.Content!),
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Uncertain_verdict_with_wrong_answer_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            Answer = "definitely-wrong",
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Uncertain_verdict_with_solved_puzzle_but_score_too_low_for_the_risk_engine_fails()
    {
        // The puzzle answer alone is not sufficient — a correctly solved puzzle still has to clear
        // the dynamic risk threshold, fed by FCaptcha's own (uncertain) trust score. 0.1 is far
        // below any realistic dynamic threshold (floor is RiskOptions.MinRequiredScore, 0.20).
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        verifier.NextResult = new FCaptchaVerification(true, 0.1);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            Answer = CaptchaTestSolvers.SolveText(challenge.Content!),
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Challenge_is_single_use()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = service.CreateChallenge();
        verifier.NextResult = new FCaptchaVerification(true, 0.9);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Null_submission_fails()
    {
        (CaptchaService service, _) = CreateService();
        Assert.False(await service.ValidateAsync(null, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Missing_challenge_id_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        verifier.NextResult = new FCaptchaVerification(true, 0.9);

        Assert.False(await service.ValidateAsync(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = "",
            FCaptchaToken = "token",
        }, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Unknown_challenge_id_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        verifier.NextResult = new FCaptchaVerification(true, 0.9);

        Assert.False(await service.ValidateAsync(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            FCaptchaToken = "token",
        }, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Solvable_maze_challenges_have_a_correct_path_that_passes_with_an_uncertain_verdict()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
            CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(service, wantSolvable: true);
            MazeDto maze = challenge.Maze!;
            List<int> path = CaptchaTestSolvers.SolveMaze(maze);

            Assert.Equal(maze.StartIndex, path[0]);
            Assert.Equal(maze.EndIndex, path[^1]);

            verifier.NextResult = new FCaptchaVerification(true, 0.6);
            HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
            {
                ChallengeId = challenge.ChallengeId,
                FCaptchaToken = "token",
                MazePath = path,
            };

            Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
        }
    }

    [Fact]
    public async Task Maze_challenge_with_wall_crossing_path_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Maze);
        MazeDto maze = challenge.Maze!;
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        // A naive path that ignores walls entirely (straight row-major walk between A and B,
        // whichever comes first) is not a valid route unless every wall along the way happens to
        // be open, which a freshly generated maze essentially never satisfies end-to-end.
        int lo = Math.Min(maze.StartIndex, maze.EndIndex);
        int hi = Math.Max(maze.StartIndex, maze.EndIndex);
        List<int> bogusPath = Enumerable.Range(lo, hi - lo + 1).ToList();
        if (maze.StartIndex > maze.EndIndex)
            bogusPath.Reverse();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            MazePath = bogusPath,
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Unsolvable_maze_challenges_are_correctly_solved_by_claiming_no_path_exists()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(service, wantSolvable: false);
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            MazeUnsolvableClaim = true,
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Claiming_a_solvable_maze_is_unsolvable_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(service, wantSolvable: true);
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            MazeUnsolvableClaim = true,
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Tracing_a_path_on_an_unsolvable_maze_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(service, wantSolvable: false);
        MazeDto maze = challenge.Maze!;
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        // There is no path from A to B in this maze, so a "path" that merely starts and ends at
        // the right cells without ever actually reaching B by open passages must still fail.
        List<int> bogusPath = [maze.StartIndex, maze.EndIndex];

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            MazePath = bogusPath,
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task TileRotate_challenge_with_correct_rotations_passes()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> clicks = CaptchaTestSolvers.SolveTileRotate(tileRotate);
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            TileRotationClicks = clicks,
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task TileRotate_challenge_with_unaligned_rotations_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> noClicks = tileRotate.Tiles.Select(_ => 0).ToList();
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            TileRotationClicks = noClicks,
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task TileRotate_challenge_with_wrong_tile_count_fails()
    {
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();
        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> tooFew = CaptchaTestSolvers.SolveTileRotate(tileRotate).Take(tileRotate.Tiles.Length - 1).ToList();
        verifier.NextResult = new FCaptchaVerification(true, 0.6);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            TileRotationClicks = tooFew,
        };

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Challenge_solved_from_the_same_ip_it_was_issued_to_passes()
    {
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        // Same accessor/context still assigned -> same resolved IP at verify time -> no IP-mismatch
        // penalty. Required score for this first-ever attempt from this identity is the base 0.50
        // plus the new-identity penalty (0.05) = 0.55; 0.6 clears it.
        verifier.NextResult = new FCaptchaVerification(true, 0.6);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Challenge_solved_from_a_different_ip_than_it_was_issued_to_fails()
    {
        // A challenge fetched from one IP but solved/submitted from another is a signal, not a
        // hard rule (see IRiskEngine): it raises the required score by 0.15 instead of an
        // automatic reject. Combined with the first-attempt new-identity penalty, the required
        // score becomes 0.50 + 0.15 + 0.05 = 0.70, clamped to the 0.68 ceiling — above the 0.6
        // trust score used here, so this still fails.
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        accessor.HttpContext = ContextWithIp("198.51.100.20");
        verifier.NextResult = new FCaptchaVerification(true, 0.6);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task A_sufficiently_strong_signal_still_passes_despite_an_ip_mismatch()
    {
        // Demonstrates the "don't block on one signal alone" design: the same IP mismatch as
        // above (raising the bar to the 0.68 ceiling) doesn't fail this attempt, because the
        // trust score here (0.69, still below AllowTrustScore so the puzzle is still required) is
        // strong enough to clear even the raised bar.
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService(accessor);

        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        accessor.HttpContext = ContextWithIp("198.51.100.20");
        verifier.NextResult = new FCaptchaVerification(true, 0.69);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            FCaptchaToken = "token",
            Answer = CaptchaTestSolvers.SolveText(challenge.Content!),
        };

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Ip_binding_is_not_enforced_when_the_client_ip_cannot_be_resolved()
    {
        // No HttpContext at all (e.g. some hosting setups, or a test double) means no IP was
        // captured at issue time, so the check degrades gracefully instead of blocking everyone.
        FakeHttpContextAccessor accessor = new() { HttpContext = null };
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        verifier.NextResult = new FCaptchaVerification(true, 0.6);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public async Task Repeated_risk_denied_attempts_raise_the_required_score_for_the_same_identity()
    {
        // Escalating friction: each risk-denied attempt from an identity raises the required
        // score for its next attempt by 0.05, up to a 0.15 cap. After three consecutive denials
        // (each a correctly-solved puzzle paired with an uncertain, too-low trust score of 0.1),
        // the required score is 0.50 + 0.15 = 0.65 — high enough that a trust score (0.62) which
        // would have comfortably passed a brand-new identity (required 0.55) no longer clears it.
        (CaptchaService service, FakeFCaptchaVerifier verifier) = CreateService();

        for (int i = 0; i < 3; i++)
        {
            CaptchaChallengeDto challenge = service.CreateChallenge();
            HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);
            verifier.NextResult = new FCaptchaVerification(true, 0.1);
            Assert.False(await service.ValidateAsync(submission, CaptchaAction.VerifyRole));
        }

        CaptchaChallengeDto finalChallenge = service.CreateChallenge();
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto finalSubmission = CorrectSubmissionFor(finalChallenge);
        verifier.NextResult = new FCaptchaVerification(true, 0.62);
        Assert.False(await service.ValidateAsync(finalSubmission, CaptchaAction.VerifyRole));
    }

    private static (CaptchaService Service, FakeFCaptchaVerifier Verifier) CreateService(IHttpContextAccessor? accessor = null)
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        IOptions<RiskOptions> options = Options.Create(new RiskOptions());
        FakeFCaptchaVerifier verifier = new();
        IRiskEngine riskEngine = new RiskEngine(
            new IdentityRiskProfileService(cache, options),
            new ScrapingDetectionService(cache),
            options);

        CaptchaService service = new(cache, verifier, riskEngine, accessor ?? new FakeHttpContextAccessor(), NullLogger<CaptchaService>.Instance);
        return (service, verifier);
    }

    private static HomeworkCentral.Api.Captcha.CaptchaSubmissionDto CorrectSubmissionFor(CaptchaChallengeDto challenge) =>
        CaptchaTestSolvers.BuildCorrectSubmission(challenge);

    private static HttpContext ContextWithIp(string ip)
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private enum CaptchaTypeOf
    {
        Text,
        Maze,
        TileRotate,
    }

    /// <summary>CreateChallenge() picks one of three types at random; retry until the requested
    /// type comes up. 60 misses in a row for a 1-in-3 type is astronomically unlikely, so this
    /// isn't flaky.</summary>
    private static CaptchaChallengeDto GetChallengeOfType(CaptchaService service, CaptchaTypeOf type)
    {
        string expected = type switch
        {
            CaptchaTypeOf.Text => "text",
            CaptchaTypeOf.Maze => "maze",
            CaptchaTypeOf.TileRotate => "tileRotate",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        for (int attempt = 0; attempt < 60; attempt++)
        {
            CaptchaChallengeDto candidate = service.CreateChallenge();
            if (candidate.Type == expected)
                return candidate;
        }

        throw new InvalidOperationException($"Could not obtain a '{expected}' challenge after 60 attempts.");
    }

    /// <summary>Maze challenges are randomly solvable (~70%) or deliberately not (~30%); retry
    /// until one matching <paramref name="wantSolvable"/> comes up. Comfortably bounded — the
    /// combined odds of drawing a maze of the wanted solvability are around 1 in 5 per attempt.</summary>
    private static CaptchaChallengeDto GetMazeChallengeWithSolvability(CaptchaService service, bool wantSolvable)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            CaptchaChallengeDto candidate = service.CreateChallenge();
            if (candidate.Type == "maze" && CaptchaTestSolvers.HasPath(candidate.Maze!) == wantSolvable)
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not obtain a maze challenge with solvable={wantSolvable} after 400 attempts.");
    }
}
