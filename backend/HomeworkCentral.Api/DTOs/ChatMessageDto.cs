using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.DTOs;

/// <summary>
/// A chat message as broadcast to every member of a room. The DTO is immutable per message
/// (it's built once and sent to all recipients, including via a single shared SignalR
/// broadcast), so it must carry the real sender identity rather than a viewer-relative
/// "is this mine" flag — ownership is derived client-side by comparing <see cref="SenderId"/>
/// to the current user. Vote aggregates are shared; <see cref="ViewerVote"/> is only populated
/// on history GET for the requesting user.
/// </summary>
public class ChatMessageDto
{
    public Guid MessageId { get; set; }
    public string RoomId { get; set; } = null!;
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    public string? SenderMessageColor { get; set; }
    public string Content { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public Guid? ReplyToSenderId { get; set; }
    public string? ReplyToSenderUsername { get; set; }
    public string? ReplyToContentSnippet { get; set; }

    public int Score { get; set; }
    public int UpvoteCount { get; set; }
    public int DownvoteCount { get; set; }
    public string? ViewerVote { get; set; }

    public List<ChatAttachmentInfoDto> Attachments { get; set; } = [];
    public ChatForwardSnapshotDto? ForwardedFrom { get; set; }
    public List<LinkPreviewDto> LinkPreviews { get; set; } = [];
}

public class ChatAttachmentInfoDto
{
    public Guid AttachmentId { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = null!;
}

public class ChatForwardSnapshotDto
{
    public string SourceRoomId { get; set; } = null!;
    public Guid SourceMessageId { get; set; }
    public Guid SourceSenderId { get; set; }
    public string SourceSenderUsername { get; set; } = null!;
    public string ContentSnippet { get; set; } = null!;
}

public class LinkPreviewDto
{
    public string Url { get; set; } = null!;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
}

public class SendChatMessageRequest
{
    [StringLength(4000)]
    public string? Content { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public List<Guid>? AttachmentIds { get; set; }
    public ChatForwardSnapshotDto? ForwardedFrom { get; set; }
}

public class CastMessageVoteRequest
{
    [Range(-1, 1)]
    public int Value { get; set; }
}

public class ChatTypingDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
}
