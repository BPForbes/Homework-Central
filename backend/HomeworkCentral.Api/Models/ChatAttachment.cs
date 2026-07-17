using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Models;

public class ChatAttachment
{
    public Guid AttachmentId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string OriginalFileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public AccountClass OwnerAccountClass { get; set; }
    public string? TenantDatabaseName { get; set; }
    public bool IsHazard { get; set; }
    public string? InlinePreviewKind { get; set; }

    public ICollection<ChatMessageAttachment> MessageLinks { get; set; } = [];
}

public class ChatMessageAttachment
{
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public Guid AttachmentId { get; set; }
    public ChatAttachment Attachment { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class ChatMessageVote
{
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public Guid UserId { get; set; }
    /// <summary>+1 upvote or -1 downvote.</summary>
    public short Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class ChatLinkPreview
{
    public Guid PreviewId { get; set; }
    public Guid MessageId { get; set; }
    public ChatMessage Message { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
