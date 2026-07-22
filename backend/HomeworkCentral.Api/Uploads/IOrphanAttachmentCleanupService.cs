namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Purges unattached uploads older than <see cref="UploadOptions.OrphanTtlHours"/> so failed
/// multi-file composer uploads do not accumulate forever on disk/DB.
/// </summary>
public interface IOrphanAttachmentCleanupService
{
    /// <summary>Deletes eligible orphan rows and files. Returns the number of attachments purged.</summary>
    Task<int> PurgeOrphansAsync(CancellationToken ct = default);
}
