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
    /// <summary>Backend-to-backend URL used for server-side token verification — inside the Docker
    /// network this is the service name (e.g. <c>http://fcaptcha:3000</c>), never a browser-facing
    /// address.</summary>
    public string ServerUrl { get; set; } = "http://fcaptcha:3000";

    /// <summary>Browser-reachable URL, sent to the frontend with every challenge so it knows where
    /// to load the widget script from and which server to configure it against.</summary>
    public string PublicUrl { get; set; } = "http://localhost:3010";

    public string SiteKey { get; set; } = "homework-central-dev";

    /// <summary>Must match the FCAPTCHA_SECRET the fcaptcha container is started with. The default
    /// here matches docker-compose.yml's dev default so local dev works out of the box — override
    /// both via environment variables with a real secret before deploying anywhere reachable.</summary>
    public string Secret { get; set; } = "dev-fcaptcha-secret-change-in-production";

    /// <summary>hCaptcha-style "I'm not a robot" checkbox — the mandatory baseline check for every
    /// captcha attempt (see <c>CaptchaService</c>).</summary>
    public bool CheckboxMode { get; set; } = true;

    /// <summary>Trust score (0..1, already normalized so higher = more human — see
    /// <c>IFCaptchaVerifier</c>) at or above which FCaptcha's own verdict is treated as sufficient
    /// on its own, with no further in-house puzzle required. Mirrors FCaptcha's documented
    /// "&lt; 0.3 raw score = Allow" band, inverted (1 - 0.3 = 0.7).</summary>
    public double AllowTrustScore { get; set; } = 0.7;
}
