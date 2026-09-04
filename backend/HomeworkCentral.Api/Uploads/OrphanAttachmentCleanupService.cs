using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed class OrphanAttachmentCleanupService(
    AppDbContext db,
    IAttachmentBlobStore blobStore,
    IOptions<UploadOptions> options) : IOrphanAttachmentCleanupService
{
    public async Task<int> PurgeOrphansAsync(CancellationToken ct = default)
    {
        UploadOptions opts = options.Value;
        DateTime cutoff = DateTime.UtcNow.AddHours(-opts.OrphanTtlHours);

        List<ChatAttachment> orphans = await db.ChatAttachments
            .Where(a => a.CreatedAtUtc < cutoff)
            .Where(a => !a.MessageLinks.Any())
            .ToListAsync(ct);

        int removed = 0;
        foreach (ChatAttachment orphan in orphans)
        {
            await blobStore.DeleteAsync(orphan.StoragePath, ct);
            db.ChatAttachments.Remove(orphan);
            removed++;
        }

        if (removed > 0)
            await db.SaveChangesAsync(ct);

        return removed;
    }
}
