using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Infrastructure;

/// <summary>
/// Shared info-room edit window used by both the read-side UI gate and write-side enforcement.
/// </summary>
public static class InfoRoomEditPolicy
{
    public const int AdminEditWindowDays = 3;

    public static bool CanEditInfoRoom(UserEffectiveMask mask, CustomChannel channel)
    {
        if (channel.RoomType != CustomRoomType.Info)
            return true;

        return CanEditInfoContent(mask.EffectiveRoleMask, channel.CreatedAtUtc);
    }

    public static bool CanEditInfoFromMask(EffectiveMaskDto masks, CustomRoomType roomType, DateTime createdAtUtc)
    {
        if (roomType != CustomRoomType.Info)
            return false;

        return CanEditInfoContent(BitMask.FromBase64(masks.RoleMask, 64), createdAtUtc);
    }

    public static bool CanEditInfoContent(BitArray roleMask, DateTime createdAtUtc)
    {
        if (BitMask.HasBit(roleMask, PlatformRoles.Owner)
            || BitMask.HasBit(roleMask, PlatformRoles.SystemAdministrator))
        {
            return true;
        }

        return BitMask.HasBit(roleMask, PlatformRoles.Administrator)
            && (DateTime.UtcNow - createdAtUtc).TotalDays <= AdminEditWindowDays;
    }
}
