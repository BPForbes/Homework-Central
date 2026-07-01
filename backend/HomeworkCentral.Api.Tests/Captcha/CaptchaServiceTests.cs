using System.Text.RegularExpressions;
using HomeworkCentral.Api.Captcha;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HomeworkCentral.Api.Tests.Captcha;

public class CaptchaServiceTests
{
    private readonly CaptchaService _service = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Correct_answer_validates_once_then_the_challenge_is_consumed()
    {
        CaptchaChallengeDto challenge = _service.CreateChallenge();
        string answer = SolveChallenge(challenge.Prompt);

        Assert.True(_service.Validate(challenge.ChallengeId, answer));
        Assert.False(_service.Validate(challenge.ChallengeId, answer));
    }

    [Fact]
    public void Wrong_answer_fails_and_also_consumes_the_challenge()
    {
        CaptchaChallengeDto challenge = _service.CreateChallenge();
        string correctAnswer = SolveChallenge(challenge.Prompt);

        Assert.False(_service.Validate(challenge.ChallengeId, "definitely-wrong"));
        Assert.False(_service.Validate(challenge.ChallengeId, correctAnswer));
    }

    [Fact]
    public void Unknown_challenge_id_fails()
    {
        Assert.False(_service.Validate(Guid.NewGuid().ToString("N"), "anything"));
    }

    [Theory]
    [InlineData(null, "answer")]
    [InlineData("challenge-id", null)]
    [InlineData("", "answer")]
    [InlineData("challenge-id", "")]
    public void Missing_challenge_id_or_answer_fails(string? challengeId, string? answer)
    {
        Assert.False(_service.Validate(challengeId, answer));
    }

    [Fact]
    public void Answer_comparison_trims_surrounding_whitespace()
    {
        CaptchaChallengeDto challenge = _service.CreateChallenge();
        string answer = SolveChallenge(challenge.Prompt);

        Assert.True(_service.Validate(challenge.ChallengeId, $"  {answer}  "));
    }

    [Fact]
    public void Code_challenge_answer_comparison_is_case_insensitive()
    {
        // Challenges alternate randomly between arithmetic and code templates; retry until a
        // code-style challenge (which has letters, so case-insensitivity is actually exercised)
        // comes up. 25 misses in a row has a ~1-in-33-million chance, so this isn't flaky.
        CaptchaChallengeDto? codeChallenge = null;
        string? answer = null;
        for (int attempt = 0; attempt < 25 && codeChallenge is null; attempt++)
        {
            CaptchaChallengeDto candidate = _service.CreateChallenge();
            Match code = Regex.Match(candidate.Prompt, @"exactly: (\w+)");
            if (code.Success)
            {
                codeChallenge = candidate;
                answer = code.Groups[1].Value;
            }
        }

        Assert.NotNull(codeChallenge);
        Assert.True(_service.Validate(codeChallenge!.ChallengeId, answer!.ToLowerInvariant()));
    }

    private static string SolveChallenge(string prompt)
    {
        Match arithmetic = Regex.Match(prompt, @"what is (\d+) \+ (\d+)\?");
        if (arithmetic.Success)
        {
            int a = int.Parse(arithmetic.Groups[1].Value);
            int b = int.Parse(arithmetic.Groups[2].Value);
            return (a + b).ToString();
        }

        Match code = Regex.Match(prompt, @"exactly: (\w+)");
        if (code.Success)
            return code.Groups[1].Value;

        throw new InvalidOperationException($"Unrecognized captcha prompt format: {prompt}");
    }
}
