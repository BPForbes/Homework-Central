using System.Net;
using System.Text.RegularExpressions;
using HomeworkCentral.Api.Captcha;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HomeworkCentral.Api.Tests.Captcha;

public class CaptchaServiceTests
{
    // No HttpContext -> ResolveClientIp() returns null -> IP binding never triggers for these
    // tests, matching pre-IP-binding behavior. IP binding itself is covered by its own tests below.
    private readonly CaptchaService _service = new(
        new MemoryCache(new MemoryCacheOptions()), new BehaviorScoringService(), new FakeHttpContextAccessor());

    [Fact]
    public void Text_challenge_with_correct_answer_and_human_like_behavior_passes()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Text);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.True(_service.Validate(submission));
    }

    [Fact]
    public void Text_challenge_is_single_use()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.True(_service.Validate(submission));
        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void Text_challenge_with_wrong_answer_fails_even_with_human_like_behavior()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = "definitely-wrong",
            Behavior = GoodBehavior(includeKeystrokes: true),
        };

        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void Correct_answer_with_bot_like_behavior_fails_the_score_gate()
    {
        // The puzzle answer alone is not sufficient — this is the core of "the input is another
        // item to pass": a correct answer submitted with bot-like telemetry (no mouse movement, no
        // interaction, webdriver flagged, implausibly fast) must still be rejected.
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Text);
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Answer = SolveText(challenge.Content!),
            Behavior = BotLikeBehavior(),
        };

        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void Null_submission_fails()
    {
        Assert.False(_service.Validate(null));
    }

    [Fact]
    public void Missing_challenge_id_fails()
    {
        Assert.False(_service.Validate(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = "",
            Answer = "anything",
            Behavior = GoodBehavior(),
        }));
    }

    [Fact]
    public void Unknown_challenge_id_fails()
    {
        Assert.False(_service.Validate(new HomeworkCentral.Api.Captcha.CaptchaSubmissionDto
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Answer = "anything",
            Behavior = GoodBehavior(),
        }));
    }

    [Fact]
    public void Maze_challenges_are_always_solvable_and_a_correct_path_with_good_behavior_passes()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Maze);
            MazeDto maze = challenge.Maze!;
            List<int> path = SolveMaze(maze);

            Assert.Equal(maze.StartIndex, path[0]);
            Assert.Equal(maze.EndIndex, path[^1]);

            HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
            {
                ChallengeId = challenge.ChallengeId,
                MazePath = path,
                Behavior = GoodBehavior(interactionCount: path.Count),
            };

            Assert.True(_service.Validate(submission));
        }
    }

    [Fact]
    public void Maze_challenge_with_wall_crossing_path_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.Maze);
        MazeDto maze = challenge.Maze!;

        // A naive path that ignores walls entirely (straight row-major walk) is not a valid route
        // unless every wall between consecutive cells happens to be open, which a freshly
        // generated maze essentially never satisfies end-to-end.
        List<int> bogusPath = Enumerable.Range(maze.StartIndex, maze.EndIndex - maze.StartIndex + 1).ToList();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            MazePath = bogusPath,
            Behavior = GoodBehavior(interactionCount: bogusPath.Count),
        };

        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void TileRotate_challenge_with_correct_rotations_and_good_behavior_passes()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> clicks = SolveTileRotate(tileRotate);

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = clicks,
            Behavior = GoodBehavior(interactionCount: clicks.Count),
        };

        Assert.True(_service.Validate(submission));
    }

    [Fact]
    public void TileRotate_challenge_with_unaligned_rotations_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> noClicks = tileRotate.Tiles.Select(_ => 0).ToList();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = noClicks,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void TileRotate_challenge_with_wrong_tile_count_fails()
    {
        CaptchaChallengeDto challenge = GetChallengeOfType(CaptchaTypeOf.TileRotate);
        TileRotateDto tileRotate = challenge.TileRotate!;
        List<int> tooFew = SolveTileRotate(tileRotate).Take(tileRotate.Tiles.Length - 1).ToList();

        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            TileRotationClicks = tooFew,
            Behavior = GoodBehavior(interactionCount: 1),
        };

        Assert.False(_service.Validate(submission));
    }

    [Fact]
    public void Challenge_solved_from_the_same_ip_it_was_issued_to_passes()
    {
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        CaptchaService service = new(new MemoryCache(new MemoryCacheOptions()), new BehaviorScoringService(), accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        // Same accessor/context still assigned -> same resolved IP at verify time.
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(service.Validate(submission));
    }

    [Fact]
    public void Challenge_solved_from_a_different_ip_than_it_was_issued_to_fails()
    {
        // A challenge fetched from one IP but solved/submitted from another looks like it was
        // farmed out to a separate solver rather than answered by the same visitor who requested it.
        FakeHttpContextAccessor accessor = new() { HttpContext = ContextWithIp("203.0.113.10") };
        CaptchaService service = new(new MemoryCache(new MemoryCacheOptions()), new BehaviorScoringService(), accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        accessor.HttpContext = ContextWithIp("198.51.100.20");
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.False(service.Validate(submission));
    }

    [Fact]
    public void Ip_binding_is_not_enforced_when_the_client_ip_cannot_be_resolved()
    {
        // No HttpContext at all (e.g. some hosting setups, or a test double) means no IP was
        // captured at issue time, so the check degrades gracefully instead of blocking everyone.
        FakeHttpContextAccessor accessor = new() { HttpContext = null };
        CaptchaService service = new(new MemoryCache(new MemoryCacheOptions()), new BehaviorScoringService(), accessor);

        CaptchaChallengeDto challenge = service.CreateChallenge();
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = CorrectSubmissionFor(challenge);

        Assert.True(service.Validate(submission));
    }

    private static HomeworkCentral.Api.Captcha.CaptchaSubmissionDto CorrectSubmissionFor(CaptchaChallengeDto challenge)
    {
        HomeworkCentral.Api.Captcha.CaptchaSubmissionDto submission = new()
        {
            ChallengeId = challenge.ChallengeId,
            Behavior = GoodBehavior(includeKeystrokes: challenge.Type == "text"),
        };

        switch (challenge.Type)
        {
            case "maze":
                submission.MazePath = SolveMaze(challenge.Maze!);
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
    private CaptchaChallengeDto GetChallengeOfType(CaptchaTypeOf type)
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
            CaptchaChallengeDto candidate = _service.CreateChallenge();
            if (candidate.Type == expected)
                return candidate;
        }

        throw new InvalidOperationException($"Could not obtain a '{expected}' challenge after 60 attempts.");
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
        tileRotate.Tiles.Select(tile => (4 - tile.InitialRotationSteps) % 4).ToList();

    /// <summary>Telemetry engineered to clear the 0.75 passing score: a wandering, variable-speed
    /// mouse path, plausible total duration, and (optionally) human-rhythm keystroke timing.</summary>
    private static HomeworkCentral.Api.Captcha.CaptchaBehaviorDto GoodBehavior(int interactionCount = 3, bool includeKeystrokes = false)
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
            TotalDurationMs = 4000,
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
