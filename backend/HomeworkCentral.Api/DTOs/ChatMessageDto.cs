using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.DTOs;

/// <summary>
/// A chat message as broadcast to every member of a room. The DTO is immutable per message
/// (it's built once and sent to all recipients, including via a single shared SignalR
/// broadcast), so it must carry the real sender identity rather than a viewer-relative
/// "is this mine" flag — ownership is derived client-side by comparing <see cref="SenderId"/>
/// to the current user.
/// </summary>
public class ChatMessageDto
{
    public Guid MessageId { get; set; }
    public string RoomId { get; set; } = null!;
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Populated only when this message is a reply to another message in the same room.</summary>
    public Guid? ReplyToMessageId { get; set; }
    public Guid? ReplyToSenderId { get; set; }
    public string? ReplyToSenderUsername { get; set; }
    public string? ReplyToContentSnippet { get; set; }
}

public class SendChatMessageRequest
{
    /// <summary>
    /// Raw message text. <see cref="HomeworkCentral.Api.Chat.ChatMessageService"/> also enforces this same length limit
    /// server-side (defense in depth), but these attributes let [ApiController]'s automatic
    /// model validation reject empty/oversized payloads with a 400 before the request even
    /// reaches the controller action, rather than falling through to a 403.
    /// </summary>
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Optional id of the message being replied to. Silently ignored (message sends as a normal,
    /// non-reply message) if it doesn't resolve to a real, visible message in the same room —
    /// this keeps a stale or cross-scope reply target from blocking the send entirely.
    /// </summary>
    public Guid? ReplyToMessageId { get; set; }
}

public class ChatTypingDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
}
