namespace HomeworkCentral.Api.DTOs;

public class ChatRoomDetailDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public string CategoryKind { get; set; } = null!;
    public bool IsPrivate { get; set; }
    /// <summary>Chat, Info, RoleClaim, or GetRoles for the built-in claim page.</summary>
    public string RoomType { get; set; } = "Chat";
    public string? InfoContent { get; set; }
    public bool CanEditInfo { get; set; }
    public Guid? CustomChannelId { get; set; }
}
