using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Deletes unattached <c>ChatAttachment</c> rows and files older than
/// <see cref="UploadOptions.OrphanTtlHours"/>. Invoked by <see cref="OrphanAttachmentCleanupWorker"/>.
/// </summary>
public sealed class OrphanAttachmentCleanupService(
    AppDbContext db,
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
            string fullPath = Path.Combine(opts.RootPath, orphan.StoragePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            db.ChatAttachments.Remove(orphan);
            removed++;
        }

        if (removed > 0)
            await db.SaveChangesAsync(ct);

        return removed;
    }
}
