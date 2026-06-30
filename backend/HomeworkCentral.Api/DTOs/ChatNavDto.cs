namespace HomeworkCentral.Api.DTOs;

public class ChatNavDto
{
    public List<ChatNavCategoryDto> Categories { get; set; } = [];
}

public class ChatNavCategoryDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<ChatNavRoomDto> Rooms { get; set; } = [];
}

public class ChatNavRoomDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}
