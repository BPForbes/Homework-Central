using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Captcha.FCaptcha;

/// <summary>
/// Validates FCaptcha widget tokens locally with <see cref="FCaptchaTokenValidator"/> using the
/// shared secret. Tokens are issued when the browser completes the widget against the public
/// FCaptcha URL; server-side verification must not call FCaptcha's <c>/api/token/verify</c>
/// endpoint because that route re-checks the verifier's TCP address and rejects tokens from our
/// split browser/API topology.
/// </summary>
public sealed class FCaptchaVerifier(
    IOptions<FCaptchaOptions> options,
    IMemoryCache cache,
    ILogger<FCaptchaVerifier> logger) : IFCaptchaVerifier
{
    public const string TypeName = "fcaptcha";
    private static readonly TimeSpan UsedTokenLifetime = TimeSpan.FromMinutes(5);

    public string SiteKey => options.Value.SiteKey;
    public string PublicUrl => options.Value.PublicUrl;
    public double AllowTrustScore => options.Value.AllowTrustScore;

    public Task<FCaptchaVerification> VerifyAsync(string? token, CancellationToken ct = default)
    {
        FCaptchaOptions opts = options.Value;
        ConcurrentDictionary<string, byte> usedSignatures = GetUsedSignatureSet();
        FCaptchaLocalVerificationResult result = FCaptchaTokenValidator.Verify(token ?? string.Empty, opts.Secret, usedSignatures);

        if (!result.IsValid)
        {
            logger.LogInformation(
                "FCaptcha locally rejected a token ({Reason}).",
                result.Reason ?? "unknown");
            return Task.FromResult(new FCaptchaVerification(false, 0.0));
        }

        // FCaptcha's own score is "higher = more bot-like" (documented bands: <0.3 allow,
        // 0.3-0.6 challenge, >0.6 block); inverted once here so every downstream consumer only
        // ever sees this codebase's usual "higher = more human" convention.
        double trustScore = Math.Clamp(1.0 - result.RawScore, 0.0, 1.0);
        return Task.FromResult(new FCaptchaVerification(true, trustScore));
    }

    private ConcurrentDictionary<string, byte> GetUsedSignatureSet()
    {
        return cache.GetOrCreate("fcaptcha:used-signatures", entry =>
        {
            entry.SlidingExpiration = UsedTokenLifetime;
            entry.AbsoluteExpirationRelativeToNow = UsedTokenLifetime;
            return new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        }) ?? new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
    }
}
