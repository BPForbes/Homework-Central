using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Security;

namespace HomeworkCentral.Api.Models;

public class ChatMessage : IScopedResource, ISanitizableContent
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
