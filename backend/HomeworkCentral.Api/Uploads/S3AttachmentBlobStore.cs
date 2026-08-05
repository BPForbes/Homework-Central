using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// S3-compatible object store (MinIO free tier / self-hosted, or any S3 API). Downloads stay
/// proxied through the API so existing auth and caution gates remain the trust boundary.
/// </summary>
public sealed class S3AttachmentBlobStore : IAttachmentBlobStore, IAsyncDisposable
{
    private readonly IAmazonS3 s3;
    private readonly UploadOptions options;
    private readonly object bucketGate = new();
    private Task? ensureBucketTask;

    public S3AttachmentBlobStore(IOptions<UploadOptions> uploadOptions)
    {
        options = uploadOptions.Value;
        if (string.IsNullOrWhiteSpace(options.S3ServiceUrl)
            || string.IsNullOrWhiteSpace(options.S3AccessKey)
            || string.IsNullOrWhiteSpace(options.S3SecretKey)
            || string.IsNullOrWhiteSpace(options.S3Bucket))
        {
            throw new InvalidOperationException(
                "Uploads:Backend=S3 requires S3ServiceUrl, S3AccessKey, S3SecretKey, and S3Bucket.");
        }

        AmazonS3Config config = new()
        {
            ServiceURL = options.S3ServiceUrl.TrimEnd('/'),
            ForcePathStyle = options.S3ForcePathStyle,
            AuthenticationRegion = string.IsNullOrWhiteSpace(options.S3Region)
                ? "us-east-1"
                : options.S3Region,
        };
        BasicAWSCredentials credentials = new(options.S3AccessKey, options.S3SecretKey);
        s3 = new AmazonS3Client(credentials, config);
    }

    public async Task PutAsync(
        string storageKey,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken ct = default)
    {
        string key = AttachmentStorageKeys.NormalizeObjectKey(storageKey);
        await EnsureBucketAsync(ct);
        PutObjectRequest request = new()
        {
            BucketName = options.S3Bucket,
            Key = key,
            InputStream = content,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
        };
        if (contentLength > 0)
            request.Headers.ContentLength = contentLength;

        await s3.PutObjectAsync(request, ct);
    }

    public async Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        if (!AttachmentStorageKeys.IsValidObjectKey(storageKey))
            return null;

        string key = AttachmentStorageKeys.NormalizeObjectKey(storageKey);
        try
        {
            await EnsureBucketAsync(ct);
            GetObjectResponse response = await s3.GetObjectAsync(options.S3Bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (!AttachmentStorageKeys.IsValidObjectKey(storageKey))
            return;

        string key = AttachmentStorageKeys.NormalizeObjectKey(storageKey);
        try
        {
            await EnsureBucketAsync(ct);
            await s3.DeleteObjectAsync(options.S3Bucket, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — orphan cleanup stays idempotent.
        }
    }

    private Task EnsureBucketAsync(CancellationToken ct)
    {
        Task? existing = Volatile.Read(ref ensureBucketTask);
        if (existing is not null)
            return existing.WaitAsync(ct);

        lock (bucketGate)
        {
            existing = ensureBucketTask;
            if (existing is not null)
                return existing.WaitAsync(ct);

            Task created = CreateBucketIfMissingAsync();
            ensureBucketTask = created;
            return created.WaitAsync(ct);
        }
    }

    private async Task CreateBucketIfMissingAsync()
    {
        ListBucketsResponse buckets = await s3.ListBucketsAsync(CancellationToken.None);
        bool exists = buckets.Buckets.Any(bucket =>
            string.Equals(bucket.BucketName, options.S3Bucket, StringComparison.Ordinal));
        if (!exists)
            await s3.PutBucketAsync(options.S3Bucket, CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        s3.Dispose();
        return ValueTask.CompletedTask;
    }
}
