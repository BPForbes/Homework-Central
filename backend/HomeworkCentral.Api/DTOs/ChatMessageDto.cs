namespace HomeworkCentral.Api.DTOs;

public class ChatMessageDto
{
    public Guid MessageId { get; set; }
    public string RoomId { get; set; } = null!;
    public Guid SenderId { get; set; }
    public string? SenderUsername { get; set; }
    public string Content { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsOwn { get; set; }
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
