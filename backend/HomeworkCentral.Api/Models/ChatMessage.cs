using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Models;

/// <summary>
/// Chat rooms are shared community spaces gated by role/expertise bits (see
/// <see cref="HomeworkCentral.Api.Chat.ChatRoomAccessService"/>), not per-tenant private data.
/// Unlike homework/grades, a chat message is intentionally NOT an <c>IScopedResource</c>:
/// each dev persona provisions its own isolated tenant database, so tenant-scoping messages
/// would make it impossible for two different personas to ever see each other's chat messages.
/// Instead, messages implement <see cref="IShareableScopedResource"/>, which applies the same
/// real-vs-developer traffic split previously hand-maintained in
/// <see cref="HomeworkCentral.Api.Chat.ChatMessageService.GetMessagesAsync"/>.
/// <see cref="OwnerAccountClass"/> and <see cref="TenantDatabaseName"/> are retained as
/// metadata (which persona/session sent the message).
///
/// <see cref="SenderId"/> intentionally has NO foreign key to <c>Users</c>: chat is always
/// persisted in the master database, but a sender's User row often lives only in their own
/// tenant database (dev personas are fully isolated per tenant), so a same-database FK
/// constraint would reject every message from a persona account. <see cref="SenderUsername"/>
/// is stored denormalized precisely so no cross-database join is ever needed to render a
/// message.
/// </summary>
public class ChatMessage : IShareableScopedResource
{
    public Guid MessageId { get; set; }
    public string RoomId { get; set; } = null!;
    public Guid SenderId { get; set; }
    public string SenderUsername { get; set; } = null!;
    /// <summary>Denormalized hex color from the sender's highest platform role at send time.</summary>
    public string? SenderMessageColor { get; set; }
    /// <summary>Raw Markdown (with embedded LaTeX and, where used, inline HTML) — the frontend renders and sanitizes it; never store an HTML-sanitized copy here, since running an HTML sanitizer over Markdown source corrupts valid syntax (angle brackets in code, etc.).</summary>
    public string RawContent { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public AccountClass OwnerAccountClass { get; set; }
    public string? TenantDatabaseName { get; set; }

    /// <summary>
    /// The message this one is replying to, if any. Sender/content are denormalized onto the
    /// reply itself (rather than requiring a join back to the parent row) so the quoted preview
    /// still renders correctly even if the parent message is ever deleted or falls outside a
    /// paginated history window.
    /// </summary>
    public Guid? ReplyToMessageId { get; set; }
    public Guid? ReplyToSenderId { get; set; }
    public string? ReplyToSenderUsername { get; set; }
    public string? ReplyToContentSnippet { get; set; }
}
