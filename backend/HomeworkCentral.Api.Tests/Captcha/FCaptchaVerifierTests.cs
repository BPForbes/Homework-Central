using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeworkCentral.Api.Captcha.FCaptcha;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HomeworkCentral.Api.Tests.Captcha;

public class FCaptchaVerifierTests
{
    private const string Secret = "integration-test-fcaptcha-secret-key!";

    [Fact]
    public async Task Valid_token_signed_with_the_shared_secret_is_accepted()
    {
        string token = CreateToken(Secret, siteKey: "homework-central-dev", rawScore: 0.2, ip: "127.0.0.1:12345");
        FCaptchaVerifier verifier = CreateVerifier();

        FCaptchaVerification verification = await verifier.VerifyAsync(token);

        Assert.True(verification.Valid);
        Assert.Equal(0.8, verification.TrustScore, precision: 3);
    }

    [Fact]
    public async Task Token_signed_with_a_different_secret_is_rejected()
    {
        string token = CreateToken("other-secret", siteKey: "homework-central-dev", rawScore: 0.2, ip: "127.0.0.1:12345");
        FCaptchaVerifier verifier = CreateVerifier();

        FCaptchaVerification verification = await verifier.VerifyAsync(token);

        Assert.False(verification.Valid);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        string token = CreateToken(
            Secret,
            siteKey: "homework-central-dev",
            rawScore: 0.2,
            ip: "127.0.0.1:12345",
            timestamp: DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds());
        FCaptchaVerifier verifier = CreateVerifier();

        FCaptchaVerification verification = await verifier.VerifyAsync(token);

        Assert.False(verification.Valid);
    }

    [Fact]
    public async Task Replayed_token_is_rejected()
    {
        string token = CreateToken(Secret, siteKey: "homework-central-dev", rawScore: 0.2, ip: "127.0.0.1:12345");
        FCaptchaVerifier verifier = CreateVerifier();

        Assert.True((await verifier.VerifyAsync(token)).Valid);
        FCaptchaVerification replay = await verifier.VerifyAsync(token);

        Assert.False(replay.Valid);
    }

    [Fact]
    public async Task Verifier_accepts_tokens_issued_to_a_different_tcp_address_than_the_api()
    {
        string token = CreateToken(Secret, siteKey: "homework-central-dev", rawScore: 0.1, ip: "172.18.0.1:45123");
        FCaptchaVerifier verifier = CreateVerifier();

        FCaptchaVerification verification = await verifier.VerifyAsync(token);

        Assert.True(verification.Valid);
        Assert.Equal(0.9, verification.TrustScore, precision: 3);
    }

    private static FCaptchaVerifier CreateVerifier()
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        IOptions<FCaptchaOptions> options = Options.Create(new FCaptchaOptions
        {
            Secret = Secret,
            SiteKey = "homework-central-dev",
            PublicUrl = "http://localhost:3010",
        });

        return new FCaptchaVerifier(options, cache, NullLogger<FCaptchaVerifier>.Instance);
    }

    private static string CreateToken(
        string secret,
        string siteKey,
        double rawScore,
        string ip,
        long? timestamp = null)
    {
        byte[] ipHash = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        string ipHashHex = Convert.ToHexString(ipHash.AsSpan(0, 4)).ToLowerInvariant();

        using JsonDocument payloadDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["ip_hash"] = ipHashHex,
                ["score"] = Math.Round(rawScore, 3),
                ["site_key"] = siteKey,
                ["timestamp"] = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }));

        byte[] payloadBytes = MarshalPayloadWithoutSignature(payloadDocument.RootElement);
        string signature = ComputeSignature(payloadBytes, secret);

        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        foreach (JsonProperty property in payloadDocument.RootElement.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
            property.WriteTo(writer);
        writer.WriteString("sig", signature);
        writer.WriteEndObject();
        writer.Flush();

        return Base64UrlEncode(stream.ToArray());
    }

    private static byte[] MarshalPayloadWithoutSignature(JsonElement root)
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        foreach (JsonProperty property in root.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
            property.WriteTo(writer);
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private static string ComputeSignature(ReadOnlySpan<byte> payload, string secret)
    {
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
