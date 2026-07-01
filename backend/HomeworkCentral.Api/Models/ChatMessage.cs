using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Security;

namespace HomeworkCentral.Api.Models;

/// <summary>
/// Chat rooms are shared community spaces gated by role/expertise bits (see
/// <see cref="HomeworkCentral.Api.Chat.ChatRoomAccessService"/>), not per-tenant private data.
/// Unlike homework/grades, a chat message is intentionally NOT an <c>IScopedResource</c>:
/// each dev persona provisions its own isolated tenant database, so tenant-scoping messages
/// would make it impossible for two different personas to ever see each other's chat messages.
/// <see cref="OwnerAccountClass"/> and <see cref="TenantDatabaseName"/> are retained only as
/// metadata (which persona/session sent the message) and to separate real production chat
/// traffic from developer/test traffic.
/// </summary>
public class ChatMessage : ISanitizableContent
{
    public Guid MessageId { get; set; }
    public string RoomId { get; set; } = null!;
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    public string RawContent { get; set; } = null!;
    public string? SanitizedContent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AccountClass OwnerAccountClass { get; set; }
    public string? TenantDatabaseName { get; set; }

    public User Sender { get; set; } = null!;
}
