using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Captcha.FCaptcha;

/// <summary>
/// Config-bound under the <c>"FCaptcha"</c> section. FCaptcha (https://github.com/WebDecoy/FCaptcha)
/// is self-hosted, not a third-party account — <see cref="ServerUrl"/> and <see cref="PublicUrl"/>
/// point at wherever that service actually runs (see docker-compose.yml's <c>fcaptcha</c> service),
/// and <see cref="SiteKey"/>/<see cref="Secret"/> are values we choose ourselves when standing it
/// up, not values issued by a provider.
/// </summary>
public sealed class FCaptchaOptions
{
    public const string DefaultDevSiteKey = "homework-central-dev";
    public const string DefaultDevSecret = "dev-fcaptcha-secret-change-in-production";

    /// <summary>Backend-to-backend URL used for server-side token verification — inside the Docker
    /// network this is the service name (e.g. <c>http://fcaptcha:3000</c>), never a browser-facing
    /// address.</summary>
    public string ServerUrl { get; set; } = "http://fcaptcha:3000";

    /// <summary>Browser-reachable URL, sent to the frontend with every challenge so it knows where
    /// to load the widget script from and which server to configure it against.</summary>
    public string PublicUrl { get; set; } = "http://localhost:3010";

    public string SiteKey { get; set; } = DefaultDevSiteKey;

    /// <summary>Must match the FCAPTCHA_SECRET the fcaptcha container is started with. Required via
    /// environment variable or user-secrets — no production fallback is checked in.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>hCaptcha-style "I'm not a robot" checkbox — the mandatory baseline check for every
    /// captcha attempt (see <c>CaptchaService</c>).</summary>
    public bool CheckboxMode { get; set; } = true;

    /// <summary>Trust score (0..1, already normalized so higher = more human — see
    /// <c>IFCaptchaVerifier</c>) at or above which FCaptcha's own verdict is treated as sufficient
    /// on its own, with no further in-house puzzle required. Mirrors FCaptcha's documented
    /// "&lt; 0.3 raw score = Allow" band, inverted (1 - 0.3 = 0.7).</summary>
    public double AllowTrustScore { get; set; } = 0.7;
}

/// <summary>Fails fast when FCaptcha is misconfigured — especially shipping dev defaults to
/// production.</summary>
public sealed class FCaptchaOptionsValidator(IHostEnvironment environment) : IValidateOptions<FCaptchaOptions>
{
    public ValidateOptionsResult Validate(string? name, FCaptchaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret))
            return ValidateOptionsResult.Fail("FCaptcha:Secret must be set via environment variable or user-secrets.");

        if (environment.IsDevelopment())
            return ValidateOptionsResult.Success;

        if (string.Equals(options.Secret, FCaptchaOptions.DefaultDevSecret, StringComparison.Ordinal))
            return ValidateOptionsResult.Fail("FCaptcha:Secret must not use the built-in dev default outside Development.");

        if (string.Equals(options.SiteKey, FCaptchaOptions.DefaultDevSiteKey, StringComparison.Ordinal))
            return ValidateOptionsResult.Fail("FCaptcha:SiteKey must not use the built-in dev default outside Development.");

        return ValidateOptionsResult.Success;
    }
}
