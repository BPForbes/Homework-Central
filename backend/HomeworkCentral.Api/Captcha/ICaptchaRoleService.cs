namespace HomeworkCentral.Api.Captcha;

/// <summary>Self-service captcha-gated promotion to <c>VerifiedUser</c> for the dashboard "Verify" button.</summary>
public interface ICaptchaRoleService
{
    /// <summary>
    /// Validates the captcha and, if it passes, strips the Guest role (if present) and grants
    /// VerifiedUser. Returns false without changing any roles if the captcha did not validate.
    /// </summary>
    Task<bool> TryVerifyAndPromoteAsync(
        Guid userId,
        CaptchaSubmissionDto submission,
        CancellationToken ct = default);
}
