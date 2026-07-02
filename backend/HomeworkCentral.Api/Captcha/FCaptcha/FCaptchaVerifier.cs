using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Captcha.FCaptcha;

/// <summary>
/// Calls a self-hosted FCaptcha instance's <c>POST /api/token/verify</c> to check a widget-issued
/// token. Registered as a typed <see cref="HttpClient"/> (see Program.cs) so the underlying socket
/// handler is pooled/reused rather than a fresh <c>HttpClient</c> per call.
/// </summary>
public sealed class FCaptchaVerifier(
    HttpClient httpClient,
    IOptions<FCaptchaOptions> options,
    ILogger<FCaptchaVerifier> logger) : IFCaptchaVerifier
{
    public const string TypeName = "fcaptcha";

    public string SiteKey => options.Value.SiteKey;
    public string PublicUrl => options.Value.PublicUrl;
    public double AllowTrustScore => options.Value.AllowTrustScore;

    public async Task<FCaptchaVerification> VerifyAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new FCaptchaVerification(false, 0.0);

        FCaptchaOptions opts = options.Value;

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                $"{opts.ServerUrl.TrimEnd('/')}/api/token/verify",
                new VerifyRequest(token, opts.Secret),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("FCaptcha token/verify returned {StatusCode}.", response.StatusCode);
                return new FCaptchaVerification(false, 0.0);
            }

            VerifyResponse? result = await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken: ct);
            if (result is null || !result.Valid)
            {
                logger.LogInformation("FCaptcha rejected a token as invalid.");
                return new FCaptchaVerification(false, 0.0);
            }

            // FCaptcha's own score is "higher = more bot-like" (documented bands: <0.3 allow,
            // 0.3-0.6 challenge, >0.6 block); inverted once here so every downstream consumer only
            // ever sees this codebase's usual "higher = more human" convention.
            double trustScore = Math.Clamp(1.0 - result.Score, 0.0, 1.0);
            return new FCaptchaVerification(true, trustScore);
        }
        catch (Exception ex)
        {
            // A network hiccup or malformed response from a self-hosted service we don't fully
            // control must fail closed (treated as "not verified") rather than crash the request
            // or, worse, silently pass.
            logger.LogWarning(ex, "FCaptcha token/verify request failed.");
            return new FCaptchaVerification(false, 0.0);
        }
    }

    private sealed record VerifyRequest(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("secret")] string Secret);

    private sealed record VerifyResponse(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("score")] double Score);
}
