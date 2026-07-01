using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Chat;

public interface IChatRoomAccessService
{
    bool CanAccessAllRooms(EffectiveMaskDto masks);

    bool CanAccessRoom(EffectiveMaskDto masks, ChatRoomDefinition room);

    bool CanAccessRoom(EffectiveMaskDto masks, string roomId);

    ChatNavDto GetAccessibleNav(EffectiveMaskDto masks);
}
