namespace HomeworkCentral.Api.Models;

/// <summary>
/// One dated post on an Info-type <see cref="CustomChannel"/>. Info rooms are a feed of these
/// rather than a single editable blob: new entries can always be appended, but each entry's own
/// edit window is enforced independently (see InfoRoomEditPolicy).
/// </summary>
public class InfoEntry
{
    public Guid EntryId { get; set; }
    public Guid ChannelId { get; set; }
    public CustomChannel Channel { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    /// <summary>Denormalized at write time so the feed doesn't need a cross-tenant user lookup to render.</summary>
    public string AuthorUsername { get; set; } = null!;
    /// <summary>Raw Markdown (with embedded LaTeX) — the frontend renders it, never the backend.</summary>
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
