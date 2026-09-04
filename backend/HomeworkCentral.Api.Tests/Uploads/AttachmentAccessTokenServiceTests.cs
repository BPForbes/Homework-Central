using System.Text;
using HomeworkCentral.Api.Uploads;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Tests.Uploads;

public class AttachmentAccessTokenServiceTests
{
    [Fact]
    public async Task TryValidateAsync_accepts_minted_token()
    {
        AttachmentAccessTokenService service = CreateService(out _);
        Guid attachmentId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        string url = service.MintDownloadUrl(attachmentId, userId);
        string accessToken = ExtractAccessToken(url);

        Assert.True(await service.TryValidateAsync(attachmentId, accessToken));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("@@@")]
    [InlineData("not-base64")]
    public async Task TryValidateAsync_malformed_base64_returns_false(string accessToken)
    {
        AttachmentAccessTokenService service = CreateService(out _);
        Assert.False(await service.TryValidateAsync(Guid.NewGuid(), accessToken));
    }

    [Fact]
    public async Task TryValidateAsync_malformed_guid_returns_false()
    {
        AttachmentAccessTokenService service = CreateService(out _);
        string token = EncodeToken("not-a-guid|00000000000000000000000000000000|9999999999|deadbeef");

        Assert.False(await service.TryValidateAsync(Guid.NewGuid(), token));
    }

    [Fact]
    public async Task TryValidateAsync_malformed_expiry_returns_false()
    {
        AttachmentAccessTokenService service = CreateService(out _);
        Guid attachmentId = Guid.NewGuid();
        string token = EncodeToken($"{attachmentId:N}|{Guid.NewGuid():N}|not-a-number|deadbeef");

        Assert.False(await service.TryValidateAsync(attachmentId, token));
    }

    [Fact]
    public async Task TryValidateAsync_wrong_attachment_id_returns_false()
    {
        AttachmentAccessTokenService service = CreateService(out _);
        string url = service.MintDownloadUrl(Guid.NewGuid(), Guid.NewGuid());
        string accessToken = ExtractAccessToken(url);

        Assert.False(await service.TryValidateAsync(Guid.NewGuid(), accessToken));
    }

    [Fact]
    public async Task TryValidateAsync_short_signature_returns_false()
    {
        AttachmentAccessTokenService service = CreateService(out _);
        Guid attachmentId = Guid.NewGuid();
        string token = EncodeToken($"{attachmentId:N}|{Guid.NewGuid():N}|9999999999|ab");

        Assert.False(await service.TryValidateAsync(attachmentId, token));
    }

    private static AttachmentAccessTokenService CreateService(out MemoryDistributedCache cache)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "unit-test-attachment-access-token-secret-key",
            })
            .Build();

        cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        IOptions<AttachmentAccessOptions> options = Options.Create(new AttachmentAccessOptions
        {
            TokenTtlMinutes = 60,
        });

        return new AttachmentAccessTokenService(config, cache, options);
    }

    private static string ExtractAccessToken(string downloadUrl)
    {
        const string marker = "accessToken=";
        int start = downloadUrl.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        string encoded = downloadUrl[(start + marker.Length)..];
        int amp = encoded.IndexOf('&', StringComparison.Ordinal);
        if (amp >= 0)
            encoded = encoded[..amp];
        return Uri.UnescapeDataString(encoded);
    }

    private static string EncodeToken(string decoded) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded));
}
