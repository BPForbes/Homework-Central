using Microsoft.Extensions.Caching.Memory;

namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Text-based captcha backed by <see cref="IMemoryCache"/>. No images — challenges are either a
/// small arithmetic question or a random verification code the user must retype, which keeps the
/// module usable from a plain HTML/React form on both the signup page and the dashboard.
/// </summary>
public sealed class CaptchaService(IMemoryCache cache) : ICaptchaService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    // Excludes visually ambiguous characters (0/O, 1/I/L) since the code is typed back by hand.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public CaptchaChallengeDto CreateChallenge()
    {
        string challengeId = Guid.NewGuid().ToString("N");
        (string label, string content, string answer) = Random.Shared.Next(2) == 0
            ? BuildArithmeticChallenge()
            : BuildCodeChallenge();

        cache.Set(CacheKey(challengeId), answer, ChallengeLifetime);
        return new CaptchaChallengeDto(challengeId, label, content);
    }

    public bool Validate(string? challengeId, string? answer)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(answer))
            return false;

        string cacheKey = CacheKey(challengeId);
        if (!cache.TryGetValue(cacheKey, out string? expected) || expected is null)
            return false;

        cache.Remove(cacheKey);
        return string.Equals(expected.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase);
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
}
