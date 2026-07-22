namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Mints and validates short-lived signed download URLs for attachments.
/// Tokens are user-bound and must not be treated as ambient authorization beyond the TTL.
/// </summary>
public interface IAttachmentAccessTokenService
{
    /// <summary>Builds a download URL with a signed access token for <paramref name="userId"/>.</summary>
    string MintDownloadUrl(Guid attachmentId, Guid userId);

    /// <summary>
    /// Returns true when the token is valid for <paramref name="attachmentId"/> and has not expired.
    /// </summary>
    Task<bool> TryValidateAsync(Guid attachmentId, string accessToken, CancellationToken ct = default);
}
