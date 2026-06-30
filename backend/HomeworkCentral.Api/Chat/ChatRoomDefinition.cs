namespace HomeworkCentral.Api.Chat;

public sealed record ChatRoomDefinition(
    string Id,
    ChatRoomKind Kind,
    string CategoryKey,
    string CategoryDisplayName,
    string RoomDisplayName,
    string? ExpertiseCategory,
    short? ExpertiseBit,
    short? RequiredRoleBit);
