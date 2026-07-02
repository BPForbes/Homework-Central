using HomeworkCentral.Api.Captcha.FCaptcha;

namespace HomeworkCentral.Api.Tests.Captcha;

/// <summary>Test double for <see cref="IFCaptchaVerifier"/> — every call to <see cref="VerifyAsync"/>
/// returns whatever <see cref="NextResult"/> is currently set to, so a test can script the exact
/// verdict it wants without standing up a real FCaptcha instance.</summary>
public sealed class FakeFCaptchaVerifier : IFCaptchaVerifier
{
    public string SiteKey { get; set; } = "test-site-key";
    public string PublicUrl { get; set; } = "http://localhost:3010";
    public double AllowTrustScore { get; set; } = 0.7;
    public FCaptchaVerification NextResult { get; set; } = new(true, 0.0);

    public Task<FCaptchaVerification> VerifyAsync(string? token, CancellationToken ct = default) =>
        Task.FromResult(NextResult);
}
