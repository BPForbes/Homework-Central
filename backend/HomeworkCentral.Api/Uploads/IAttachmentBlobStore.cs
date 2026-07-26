namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Byte store for chat/ticket attachments. Metadata stays in PostgreSQL; this interface
/// only moves object bytes (local disk or S3-compatible MinIO).
/// </summary>
public interface IAttachmentBlobStore
{
    Task PutAsync(
        string storageKey,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
