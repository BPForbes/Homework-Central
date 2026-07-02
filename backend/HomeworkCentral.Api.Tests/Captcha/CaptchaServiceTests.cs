using System.Net;
using System.Text.RegularExpressions;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.ArrowMatch;
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
    // No HttpContext -> ResolveClientIp() returns null -> IP binding never triggers for these
    // tests, matching pre-IP-binding behavior. IP binding itself is covered by its own tests below.
    private readonly CaptchaService _service = CreateService();

    [Fact]
    public void Text_challenge_with_correct_answer_and_human_like_behavior_passes()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.Text);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.True(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Text_challenge_is_single_use()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.True(_service.Validate(submission, CaptchaAction.VerifyRole));
        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Text_challenge_with_wrong_answer_fails_even_with_human_like_behavior()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = "definitely-wrong",
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Correct_answer_with_bot_like_behavior_fails_the_score_gate()
    {
        // The puzzle answer alone is not sufficient — this is the core of "the input is another
        // item to pass": a correct answer submitted with bot-like telemetry (no mouse movement, no
        // interaction, webdriver flagged, implausibly fast) must still be rejected. Its computed
        // score clamps to 0.0, far below any realistic dynamic threshold (which is clamped to a
        // minimum of RiskOptions.MinRequiredScore, 0.35 by default).
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = BotLikeBehavior(),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Null_submission_fails()
    {
        Assert.False(_service.Validate(null, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Missing_challenge_id_fails()
    {
        Assert.False(_service.Validate(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = "",
            Answer = "anything",
            Behavior = GoodBehavior(),
        }, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Unknown_challenge_id_fails()
    {
        Assert.False(_service.Validate(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Answer = "anything",
            Behavior = GoodBehavior(),
        }, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Solvable_maze_challenges_have_a_correct_path_that_passes_when_traced_with_good_behavior()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(_service, wantSolvable: true);
            MazeDto maze = challenge.Maze!;
            List<int> path = SolveMaze(maze);

            Assert.Equal(maze.StartIndex, path[0]);
            Assert.Equal(maze.EndIndex, path[^1]);

            HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
            {
                ChallengeId = challenge.ChallengeId,
                MazePath = path,
                // Mazes up to 11x11 can have solution paths long enough that a fixed 4s duration
                // would look like an implausibly fast interaction rate (BehaviorScoringService
                // penalizes >20 interactions/sec); scale duration with path length so a long,
                // legitimately-solved path doesn't fail the behavioral gate on that basis alone.
                Behavior = GoodBehavior(interactionCount: path.Count, totalDurationMs: Math.Max(4000, path.Count * 150)),
            };

            Assert.True(_service.Validate(submission, CaptchaAction.VerifyRole));
        }
    }

    [Fact]
    public void Maze_challenge_with_wall_crossing_path_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.Maze);
        MazeDto maze = challenge.Maze!;

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
            MazePath = bogusPath,
            Behavior = GoodBehavior(interactionCount: bogusPath.Count),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Unsolvable_maze_challenges_are_correctly_solved_by_claiming_no_path_exists()
    {
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(_service, wantSolvable: false);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            MazeUnsolvableClaim = true,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.True(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Claiming_a_solvable_maze_is_unsolvable_fails()
    {
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(_service, wantSolvable: true);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            MazeUnsolvableClaim = true,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Tracing_a_path_on_an_unsolvable_maze_fails()
    {
        CaptchaChallengeDto challenge = GetMazeChallengeWithSolvability(_service, wantSolvable: false);
        MazeDto maze = challenge.Maze!;

        // There is no path from A to B in this maze, so a "path" that merely starts and ends at
        // the right cells without ever actually reaching B by open passages must still fail.
        List<int> bogusPath = [maze.StartIndex, maze.EndIndex];

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            MazePath = bogusPath,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void TileRotate_challenge_with_correct_rotations_and_good_behavior_passes()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> clicks = SolveTileRotate(tileRotate);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = clicks,
            Behavior = GoodBehavior(interactionCount: clicks.Count),
        };

        Assert.True(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void TileRotate_challenge_with_unaligned_rotations_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> noClicks = tileRotate.Tiles.Select(_ => 0).ToList();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = noClicks,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void TileRotate_challenge_with_wrong_tile_count_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(_service, CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> tooFew = SolveTileRotate(tileRotate).Take(tileRotate.Tiles.Length - 1).ToList();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = tooFew,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Challenge_solved_from_the_same_ip_it_was_issued_to_passes()
    {
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        CaptchaService service = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        // Same accessor/context still assigned -> same resolved IP at verify time -> no IP-mismatch
        // penalty. Required score for this first-ever attempt from this identity is the base 0.75
        // plus the small new-identity penalty (0.05) = 0.80; CorrectSubmissionFor's telemetry
        // scores exactly 0.85 regardless of challenge type, clearing it.
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Challenge_solved_from_a_different_ip_than_it_was_issued_to_fails()
    {
        // A challenge fetched from one IP but solved/submitted from another is a signal, not a
        // hard rule (see IRiskEngine): it raises the required score by 0.20 instead of an
        // automatic reject. Combined with the first-attempt new-identity penalty, the required
        // score becomes 0.75 + 0.20 + 0.05 = 1.00, clamped to the 0.95 ceiling — well above the
        // 0.85 that CorrectSubmissionFor's telemetry scores, so this still fails today. The next
        // test shows the same mismatch can be outweighed by a stronger signal.
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        CaptchaService service = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        accessor.HttpContext = ContextWithIp("198.51.100.20");
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.False(service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void A_sufficiently_strong_signal_still_passes_despite_an_ip_mismatch()
    {
        // Demonstrates the "don't block on one signal alone" design: the same IP mismatch as
        // above (raising the bar to 0.95) doesn't fail this attempt, because a text challenge
        // solved with keystroke telemetry included scores a full 1.00.
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        CaptchaService service = CreateService(accessor);

        CaptchaChallengeDto challenge = GetChallengeOfType(service, CaptchaTypeOf.Text);
        accessor.HttpContext = ContextWithIp("198.51.100.20");
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.True(service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Ip_binding_is_not_enforced_when_the_client_ip_cannot_be_resolved()
    {
        // No HttpContext at all (e.g. some hosting setups, or a test double) means no IP was
        // captured at issue time, so the check degrades gracefully instead of blocking everyone.
        FakeHttpContextAccessor accessor = new() { HttpContext = null };
        CaptchaService service = CreateService(accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(service.Validate(submission, CaptchaAction.VerifyRole));
    }

    [Fact]
    public void Repeated_risk_denied_attempts_raise_the_required_score_for_the_same_identity()
    {
        // Escalating friction: each risk-denied attempt from an identity raises the required
        // score for its next attempt by 0.05, up to a 0.20 cap. After three consecutive denials,
        // the required score is 0.75 + 0.15 = 0.90 — high enough that telemetry which would have
        // comfortably passed a brand-new identity (0.85, against a first-attempt bar of 0.80) no
        // longer clears it.
        CaptchaService service = CreateService();

        for (int i = 0; i < 3; i++)
        {
            CaptchaChallengeDto challenge = service.CreateChallenge();
            HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);
            submission.Behavior = BotLikeBehavior();
            Assert.False(service.Validate(submission, CaptchaAction.VerifyRole));
        }

        CaptchaChallengeDto finalChallenge = service.CreateChallenge();
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto finalSubmission = CorrectSubmissionFor(finalChallenge);
        Assert.False(service.Validate(finalSubmission, CaptchaAction.VerifyRole));
    }

    private static CaptchaService CreateService(IHttpContextAccessor? accessor = null)
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        IOptions<RiskOptions> options = Options.Create(new RiskOptions());
        IRiskEngine riskEngine = new RiskEngine(
            new BehaviorScoringService(),
            new IdentityRiskProfileService(cache, options),
            new ScrapingDetectionService(cache),
            options);

        return new CaptchaService(cache, riskEngine, accessor ?? new FakeHttpContextAccessor(), NullLogger<CaptchaService>.Instance);
    }

    /// <summary>Solves whichever challenge type was issued with telemetry that scores exactly 0.85
    /// regardless of type (baseline 0.5 + mouse-movement 0.2 + duration 0.1 + interaction 0.05,
    /// keystrokes deliberately omitted so text challenges don't score any higher than maze/tile —
    /// see <see cref="GoodBehavior"/>). Deterministic and type-independent, so tests using it don't
    /// depend on which of the three challenge types <see cref="CaptchaService.CreateChallenge"/>
    /// happened to draw.</summary>
    private static HomeworkCentral.Api.Captcha.CaptchaSubmissionDto CorrectSubmissionFor(CaptchaChallengeDto challenge)
    {
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Behavior = GoodBehavior(),
        };

        switch (challenge.Type)
        {
            case "maze" when HasPath(challenge.Maze!):
                submission.MazePath = SolveMaze(challenge.Maze!);
                break;
            case "maze":
                // Some maze challenges are deliberately generated with no path from A to B at
                // all; correctly recognizing that is itself the correct answer.
                submission.MazeUnsolvableClaim = true;
                break;
            case "tileRotate":
                submission.TileRotationClicks = SolveTileRotate(challenge.TileRotate!);
                break;
            default:
                submission.Answer = SolveText(challenge.Content!);
                break;
        }

        return submission;
    }

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
            if (candidate.Type == "maze" && HasPath(candidate.Maze!) == wantSolvable)
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not obtain a maze challenge with solvable={wantSolvable} after 400 attempts.");
    }

    private static bool HasPath(MazeDto maze)
    {
        if (maze.StartIndex == maze.EndIndex)
            return true;

        bool[] visited = new bool[maze.CellWalls.Length];
        Queue<int> queue = new();
        queue.Enqueue(maze.StartIndex);
        visited[maze.StartIndex] = true;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == maze.EndIndex)
                return true;

            foreach (int neighbor in MazeNeighbors(maze, current))
            {
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private static string SolveText(string content)
    {
        Match arithmetic = Regex.Match(content, @"^(\d+) \+ (\d+)$");
        if (arithmetic.Success)
        {
            int a = int.Parse(arithmetic.Groups[1].Value);
            int b = int.Parse(arithmetic.Groups[2].Value);
            return (a + b).ToString();
        }

        return content;
    }

    private static List<int> SolveMaze(MazeDto maze)
    {
        int cellCount = maze.Width * maze.Height;
        int[] previous = new int[cellCount];
        Array.Fill(previous, -1);
        bool[] visited = new bool[cellCount];
        Queue<int> queue = new();
        queue.Enqueue(maze.StartIndex);
        visited[maze.StartIndex] = true;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == maze.EndIndex)
                break;

            foreach (int neighbor in MazeNeighbors(maze, current))
            {
                if (visited[neighbor])
                    continue;

                visited[neighbor] = true;
                previous[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        List<int> reversed = new();
        int step = maze.EndIndex;
        while (step != maze.StartIndex)
        {
            reversed.Add(step);
            step = previous[step];
        }
        reversed.Add(maze.StartIndex);
        reversed.Reverse();
        return reversed;
    }

    private static IEnumerable<int> MazeNeighbors(MazeDto maze, int cell)
    {
        int walls = maze.CellWalls[cell];
        int x = cell % maze.Width;
        int y = cell / maze.Width;

        if ((walls & 1) != 0 && y > 0) yield return cell - maze.Width; // North
        if ((walls & 2) != 0 && x < maze.Width - 1) yield return cell + 1; // East
        if ((walls & 4) != 0 && y < maze.Height - 1) yield return cell + maze.Width; // South
        if ((walls & 8) != 0 && x > 0) yield return cell - 1; // West
    }

    private static List<int> SolveTileRotate(TileRotateDto tileRotate) =>
        tileRotate.Tiles.Select(tile => (tile.TargetRotationSteps - tile.InitialRotationSteps + 8) % 8).ToList();

    /// <summary>Telemetry engineered around a fixed, hand-verified mouse path: a wandering,
    /// variable-speed motion (directness ratio ~0.72, speed stddev ~0.57) worth +0.2, a plausible
    /// 4s duration worth +0.1, and (with default interaction counts) an interaction rate worth
    /// +0.05, for exactly 0.85 on top of the 0.5 baseline. Adding keystrokes worth +0.15 (stddev
    /// ~44ms, inside the human-rhythm band) brings a text-challenge submission to exactly 1.00.</summary>
    private static HomeworkCentral.Api.Captcha.CaptchaBehaviorDto GoodBehavior(
        int interactionCount = 3, bool includeKeystrokes = false, int totalDurationMs = 4000)
    {
        int[] dxs = [3, 15, -2, 20, -1, 18, 2, 16, -3, 14];
        int[] dys = [2, -10, 3, -12, 2, 9, -1, -11, 4, 8];
        int[] dts = [80, 20, 90, 15, 85, 18, 95, 16, 88, 22];

        List<MouseSampleDto> mouseSamples = new();
        int x = 10, y = 10, t = 0;
        for (int i = 0; i < dxs.Length; i++)
        {
            x += dxs[i];
            y += dys[i];
            t += dts[i];
            mouseSamples.Add(new MouseSampleDto { X = x, Y = y, TMs = t });
        }

        HomeworkCentral.Api.Captcha.CaptchaBehaviorDto behavior = new()
        {
            MouseSamples = mouseSamples,
            TotalDurationMs = totalDurationMs,
            WebdriverFlag = false,
            InteractionCount = interactionCount,
        };

        if (includeKeystrokes)
            behavior.KeyIntervalsMs = [120, 95, 180, 60, 140, 110, 200, 85];

        return behavior;
    }

    private static HomeworkCentral.Api.Captcha.CaptchaBehaviorDto BotLikeBehavior() => new()
    {
        MouseSamples = null,
        KeyIntervalsMs = null,
        TotalDurationMs = 150,
        WebdriverFlag = true,
        InteractionCount = 0,
    };
}
