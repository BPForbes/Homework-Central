namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Purges unattached uploads past <see cref="UploadOptions.OrphanTtlHours"/> so abandoned
/// composer uploads (e.g. partial multi-file failures) do not accumulate on disk/DB.
/// </summary>
public interface IOrphanAttachmentCleanupService
{
    Task<int> PurgeOrphansAsync(CancellationToken ct = default);
}
