namespace HomeworkCentral.Api.Uploads;

/// <summary>Filesystem upload limits and orphan cleanup schedule (config section <c>Uploads</c>).</summary>
public class UploadOptions
{
    public string RootPath { get; set; } = "App_Data/uploads";
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Hours before an upload with no message link is eligible for purge.</summary>
    public int OrphanTtlHours { get; set; } = 24;

    public int CleanupIntervalMinutes { get; set; } = 60;
}
