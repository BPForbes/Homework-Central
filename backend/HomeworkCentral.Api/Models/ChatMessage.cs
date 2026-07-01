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
///
/// <see cref="SenderId"/> intentionally has NO foreign key to <c>Users</c>: chat is always
/// persisted in the master database, but a sender's User row often lives only in their own
/// tenant database (dev personas are fully isolated per tenant), so a same-database FK
/// constraint would reject every message from a persona account. <see cref="SenderUsername"/>
/// is stored denormalized precisely so no cross-database join is ever needed to render a
/// message.
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
}
