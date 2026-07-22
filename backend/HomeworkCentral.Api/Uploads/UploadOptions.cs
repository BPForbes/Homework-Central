namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Local filesystem upload limits and orphan cleanup schedule. Bound from configuration
/// section <c>Uploads</c>. See docs/chat.md and README ClamAV/dev notes.
/// </summary>
public class UploadOptions
{
    /// <summary>Directory under the content root where attachment files are stored.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";

    /// <summary>Maximum accepted upload size in bytes (default 10 MiB).</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Hours after upload before an unattached attachment is eligible for purge
    /// (orphans that were never linked to a message).
    /// </summary>
    public int OrphanTtlHours { get; set; } = 24;

    /// <summary>Background cleanup poll interval in minutes.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;
}
