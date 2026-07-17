namespace HomeworkCentral.Api.Uploads;

public interface IOrphanAttachmentCleanupService
{
    Task<int> PurgeOrphansAsync(CancellationToken ct = default);
}
