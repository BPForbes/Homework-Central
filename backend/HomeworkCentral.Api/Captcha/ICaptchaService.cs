namespace HomeworkCentral.Api.Captcha;

public sealed record CaptchaChallengeDto(string ChallengeId, string Prompt);

/// <summary>
/// Issues short-lived, single-use text captcha challenges and validates submitted answers.
/// Used both by signup (to grant <c>VerifiedUser</c> instead of <c>Guest</c>) and by the
/// dashboard "Verify" button (to promote an existing Guest).
/// </summary>
public interface ICaptchaService
{
    CaptchaChallengeDto CreateChallenge();

    /// <summary>Validates and consumes a challenge. Each challenge can only be checked once,
    /// regardless of outcome, so a captured answer cannot be replayed.</summary>
    bool Validate(string? challengeId, string? answer);
}
