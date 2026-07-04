namespace HomeworkCentral.Api.Captcha.FCaptcha;

/// <summary><see cref="TrustScore"/> is already normalized to this codebase's "higher = more
/// human" convention — FCaptcha's own raw score is the opposite (higher = more bot-like), inverted
/// once here at the boundary so nothing downstream has to remember which way it points.</summary>
public sealed record FCaptchaVerification(bool Valid, double TrustScore);

/// <summary>
/// The mandatory baseline "I'm not a robot" check, backed by a self-hosted FCaptcha instance
/// (https://github.com/WebDecoy/FCaptcha) rather than a third-party account. Every captcha
/// challenge requires a valid FCaptcha token; a confidently-human verdict is accepted on its own,
/// and an uncertain one falls back to also requiring one of the in-house puzzles — see
/// <c>HomeworkCentral.Api.Captcha.CaptchaService</c>.
/// </summary>
public interface IFCaptchaVerifier
{
    /// <summary>Public site key the frontend renders the widget with. Not a secret — safe to send
    /// to the client on every challenge.</summary>
    string SiteKey { get; }

    /// <summary>Browser-reachable URL the frontend loads the widget script from.</summary>
    string PublicUrl { get; }

    /// <summary>Trust score at/above which FCaptcha's own verdict is accepted without requiring an
    /// in-house puzzle too.</summary>
    double AllowTrustScore { get; }

    Task<FCaptchaVerification> VerifyAsync(string? token, CancellationToken ct = default);
}
