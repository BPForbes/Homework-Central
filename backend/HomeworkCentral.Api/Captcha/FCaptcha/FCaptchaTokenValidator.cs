using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeworkCentral.Api.Captcha.FCaptcha;

/// <summary>
/// Verifies FCaptcha widget tokens locally with the shared secret. The self-hosted server's
/// <c>POST /api/token/verify</c> endpoint binds tokens to the caller's TCP connection address,
/// which breaks our split architecture (browser issues tokens via the public URL; the API verifies
/// from a separate backend connection). Signature, expiry, and replay checks are performed here
/// instead so verification succeeds without relying on that HTTP endpoint.
/// </summary>
internal static class FCaptchaTokenValidator
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    public static FCaptchaLocalVerificationResult Verify(string token, string secret, ConcurrentDictionary<string, byte> usedSignatures)
    {
        if (string.IsNullOrWhiteSpace(token))
            return FCaptchaLocalVerificationResult.FromInvalid("empty_token");

        byte[] decoded;
        try
        {
            decoded = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return FCaptchaLocalVerificationResult.FromInvalid("invalid_encoding");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(decoded);
        }
        catch (JsonException)
        {
            return FCaptchaLocalVerificationResult.FromInvalid("invalid_json");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FCaptchaLocalVerificationResult.FromInvalid("invalid_json");

            if (!root.TryGetProperty("timestamp", out JsonElement timestampElement)
                || !timestampElement.TryGetInt64(out long timestamp))
            {
                return FCaptchaLocalVerificationResult.FromInvalid("missing_timestamp");
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp > (long)TokenLifetime.TotalSeconds)
                return FCaptchaLocalVerificationResult.FromInvalid("expired");

            if (!root.TryGetProperty("sig", out JsonElement sigElement)
                || sigElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(sigElement.GetString()))
            {
                return FCaptchaLocalVerificationResult.FromInvalid("missing_signature");
            }

            string signature = sigElement.GetString()!;
            byte[] payload = MarshalPayloadWithoutSignature(root);
            string expectedSignature = ComputeSignature(payload, secret);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(signature),
                    Encoding.UTF8.GetBytes(expectedSignature)))
            {
                return FCaptchaLocalVerificationResult.FromInvalid("invalid_signature");
            }

            if (!usedSignatures.TryAdd(signature, 0))
                return FCaptchaLocalVerificationResult.FromInvalid("token_already_used");

            if (!root.TryGetProperty("score", out JsonElement scoreElement)
                || !scoreElement.TryGetDouble(out double score))
            {
                usedSignatures.TryRemove(signature, out _);
                return FCaptchaLocalVerificationResult.FromInvalid("missing_score");
            }
            return FCaptchaLocalVerificationResult.FromValid(score);
        }
    }

    internal static byte[] MarshalPayloadWithoutSignature(JsonElement root)
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();

        foreach (JsonProperty property in root.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            if (property.NameEquals("sig"))
                continue;

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    internal static string ComputeSignature(ReadOnlySpan<byte> payload, string secret)
    {
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] Base64UrlDecode(string token)
    {
        string padded = token.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }
}

internal readonly record struct FCaptchaLocalVerificationResult(bool IsValid, double RawScore, string? Reason)
{
    public static FCaptchaLocalVerificationResult FromValid(double rawScore) => new(true, rawScore, null);

    public static FCaptchaLocalVerificationResult FromInvalid(string reason) => new(false, 0.0, reason);
}
