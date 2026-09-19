namespace HomeworkCentral.Api.Uploads;

/// <summary>Upload limits, orphan cleanup, and blob backend (config section <c>Uploads</c>).</summary>
public class UploadOptions
{
    /// <summary><c>Local</c> disk under <see cref="RootPath"/>, or <c>S3</c> (MinIO / S3 API).</summary>
    public string Backend { get; set; } = "Local";

    public string RootPath { get; set; } = "App_Data/uploads";
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Hours before an upload with no message link is eligible for purge.</summary>
    public int OrphanTtlHours { get; set; } = 24;

    public int CleanupIntervalMinutes { get; set; } = 60;

    public string? S3ServiceUrl { get; set; }
    public string S3Bucket { get; set; } = "homework-central-uploads";
    public string? S3AccessKey { get; set; }
    public string? S3SecretKey { get; set; }
    public string S3Region { get; set; } = "us-east-1";

    /// <summary>Required for MinIO and most self-hosted S3 gateways.</summary>
    public bool S3ForcePathStyle { get; set; } = true;

    public bool UsesS3Backend =>
        string.Equals(Backend, "S3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Backend, "Minio", StringComparison.OrdinalIgnoreCase);
}
