namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// <paramref name="Label"/> is plain instructional text, safe to select/copy. <paramref name="Content"/>
/// is the security-relevant part (the code to retype, or the expression to solve) — the frontend
/// renders it distorted and blocks selection/copy so it can't just be lifted with a click-drag or
/// Ctrl+C into the answer field.
/// </summary>
public sealed record CaptchaChallengeDto(string ChallengeId, string Label, string Content);

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
