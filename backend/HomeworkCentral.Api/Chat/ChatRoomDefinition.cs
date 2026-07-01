namespace HomeworkCentral.Api.Chat;

public sealed record ChatRoomDefinition(
    string Id,
    ChatRoomKind Kind,
    string CategoryKey,
    string CategoryDisplayName,
    ChatCategoryKind CategoryKind,
    string RoomDisplayName,
    bool IsPrivate,
    string? ExpertiseCategory,
    short? ExpertiseBit,
    short? RequiredRoleBit);
