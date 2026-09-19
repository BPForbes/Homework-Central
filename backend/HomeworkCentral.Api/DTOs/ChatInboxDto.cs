namespace HomeworkCentral.Api.DTOs;

public class ChatInboxItemDto
{
    public Guid NotificationId { get; set; }
    public Guid MessageId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string RoomDisplayName { get; set; } = null!;
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public string MessageContent { get; set; } = null!;
    public string MentionKind { get; set; } = null!;
    public Guid? TicketId { get; set; }
    public string? TicketStatus { get; set; }
    public List<TicketIntakeAnswerDto>? TicketIntakeAnswers { get; set; }
    public string? TicketDecision { get; set; }
    public string? TicketDecisionSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public bool IsRead => ReadAtUtc is not null;
}

public class ChatInboxSummaryItemDto
{
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public int UnreadCount { get; set; }
}

public class ChatInboxSummaryDto
{
    public IReadOnlyList<ChatInboxSummaryItemDto> Categories { get; set; } = [];
}

public class DeleteInboxItemsRequest
{
    public IReadOnlyList<Guid> NotificationIds { get; set; } = [];
}

public class SendChatMessageErrorResponse
{
    public string Message { get; set; } = null!;
    public string? Code { get; set; }
    public int? RetryAfterSeconds { get; set; }
}
