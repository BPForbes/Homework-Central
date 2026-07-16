using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Chat;

public interface IChatRoomAccessService
{
    bool CanAccessAllRooms(EffectiveMaskDto masks);

    bool CanAccessRoom(EffectiveMaskDto masks, ChatRoomDefinition room);

    bool CanAccessRoom(EffectiveMaskDto masks, string roomId);

    bool CanAccessRoom(EffectiveMaskDto masks, Guid userId, string roomId);

    ChatNavDto GetAccessibleNav(EffectiveMaskDto masks, Guid userId);
}
