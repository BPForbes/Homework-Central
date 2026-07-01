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
}

public class SendChatMessageRequest
{
    public string Content { get; set; } = null!;
}

public class ChatTypingDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
}
