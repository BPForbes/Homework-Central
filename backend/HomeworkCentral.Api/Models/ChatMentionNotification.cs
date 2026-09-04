using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Security;

namespace HomeworkCentral.Api.Models;

/// <summary>
/// A mention notification delivered to a user's inbox when they are @-mentioned in chat.
/// Stored in the master database alongside <see cref="ChatMessage"/>.
/// </summary>
public class ChatMentionNotification : IShareableScopedResource
{
    public Guid NotificationId { get; set; }
    public Guid MessageId { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public string RoomDisplayName { get; set; } = null!;
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public string MessageContent { get; set; } = null!;
    public string MentionKind { get; set; } = null!;
    public Guid? TicketId { get; set; }
    public string? TicketPayloadJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public AccountClass OwnerAccountClass { get; set; }
    public string? TenantDatabaseName { get; set; }
}
