namespace HomeworkCentral.Api.DTOs;

public class ChatNavDto
{
    public List<ChatNavCategoryDto> Categories { get; set; } = [];
}

public class ChatNavCategoryDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string CategoryKind { get; set; } = null!;
    public bool IsPrivateCategory { get; set; }
    public List<ChatNavRoomDto> Rooms { get; set; } = [];
}

public class ChatNavRoomDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsPrivate { get; set; }
    public string CategoryKey { get; set; } = null!;
    public string CategoryKind { get; set; } = null!;
    public string RoomType { get; set; } = "Chat";
}
