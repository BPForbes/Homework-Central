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
        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(accessToken));
        string[] parts = decoded.Split('|');

        if (parts.Length != 4)
            return false;

        Guid tokenAttachmentId = Guid.Parse(parts[0]);
        long expiresUnix = long.Parse(parts[2]);
        string payload = $"{parts[0]}|{parts[1]}|{parts[2]}";
        string signature = parts[3];

        bool tokenShapeValid = (tokenAttachmentId, expiresUnix, signature) switch
        {
            (Guid id, _, _) when id != attachmentId => false,
            (_, long exp, _) when DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp => false,
            (_, _, string sig) when !SignatureValid(payload, sig) => false,
            _ => true,
        };
        if (!tokenShapeValid)
            return false;

        string? cached = await cache.GetStringAsync($"att:tok:{attachmentId:N}:{signature}", ct);
        return cached is not null;
    }

    private bool SignatureValid(string payload, string signature)
    {
        byte[] expected = Encoding.UTF8.GetBytes(Sign(payload));
        byte[] actual = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
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
