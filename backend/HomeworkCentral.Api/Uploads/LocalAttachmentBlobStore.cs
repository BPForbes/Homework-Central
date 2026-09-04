using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed class LocalAttachmentBlobStore(IOptions<UploadOptions> options) : IAttachmentBlobStore
{
    public async Task PutAsync(
        string storageKey,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default)
    {
        _ = contentType;
        _ = contentLength;
        UploadOptions uploadOptions = options.Value;
        Directory.CreateDirectory(uploadOptions.RootPath);
        if (!AttachmentStorageKeys.TryResolveLocalPath(uploadOptions.RootPath, storageKey, out string fullPath))
            throw new InvalidOperationException("Attachment storage path is invalid.");

        await using FileStream fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UploadOptions uploadOptions = options.Value;
        if (!AttachmentStorageKeys.TryResolveLocalPath(uploadOptions.RootPath, storageKey, out string fullPath)
            || !File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UploadOptions uploadOptions = options.Value;
        if (AttachmentStorageKeys.TryResolveLocalPath(uploadOptions.RootPath, storageKey, out string fullPath)
            && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
