using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed class AttachmentAccessTokenService(
    IConfiguration config,
    IDistributedCache cache,
    IOptions<AttachmentAccessOptions> options) : IAttachmentAccessTokenService
{
    public string MintDownloadUrl(Guid attachmentId, Guid userId)
    {
        AttachmentAccessOptions opts = options.Value;
        long expiresUnix = DateTimeOffset.UtcNow.AddMinutes(opts.TokenTtlMinutes).ToUnixTimeSeconds();
        string payload = $"{attachmentId:N}|{userId:N}|{expiresUnix}";
        string signature = Sign(payload);
        string accessToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{signature}"));

        DistributedCacheEntryOptions cacheOpts = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(opts.TokenTtlMinutes),
        };
        cache.SetString($"att:tok:{attachmentId:N}:{signature}", userId.ToString(), cacheOpts);

        return $"/api/chat/attachments/{attachmentId}?accessToken={Uri.EscapeDataString(accessToken)}";
    }

    public async Task<bool> TryValidateAsync(Guid attachmentId, string accessToken, CancellationToken ct = default)
    {
        // Anonymous download URLs accept arbitrary query input; malformed Base64/GUID/expiry
        // must return false (401) rather than throw FormatException (500).
        if (!TryParseToken(accessToken, out string attachmentIdText, out string userIdText, out string expiresText, out string signature))
            return false;

        if (!Guid.TryParse(attachmentIdText, out Guid tokenAttachmentId)
            || tokenAttachmentId != attachmentId
            || !long.TryParse(expiresText, out long expiresUnix)
            || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnix)
        {
            return false;
        }

        string payload = $"{attachmentIdText}|{userIdText}|{expiresText}";
        if (!SignatureValid(payload, signature))
            return false;

        string? cached = await cache.GetStringAsync($"att:tok:{attachmentId:N}:{signature}", ct);
        return cached is not null;
    }

    private static bool TryParseToken(
        string accessToken,
        out string attachmentIdText,
        out string userIdText,
        out string expiresText,
        out string signature)
    {
        attachmentIdText = string.Empty;
        userIdText = string.Empty;
        expiresText = string.Empty;
        signature = string.Empty;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(accessToken);
        }
        catch (FormatException)
        {
            return false;
        }

        string[] parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 4)
            return false;

        attachmentIdText = parts[0];
        userIdText = parts[1];
        expiresText = parts[2];
        signature = parts[3];
        return true;
    }

    private bool SignatureValid(string payload, string signature)
    {
        byte[] expected = Encoding.UTF8.GetBytes(Sign(payload));
        byte[] actual = Encoding.UTF8.GetBytes(signature);
        // FixedTimeEquals throws when lengths differ; treat that as an invalid token.
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(string payload)
    {
        string secret = config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
