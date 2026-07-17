namespace HomeworkCentral.Api.Uploads;

public interface IAttachmentAccessTokenService
{
    string MintDownloadUrl(Guid attachmentId, Guid userId);
    Task<bool> TryValidateAsync(Guid attachmentId, string accessToken, CancellationToken ct = default);
}
