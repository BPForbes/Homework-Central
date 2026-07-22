using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Chat.Mentions;

/// <summary>Role and mention permission helpers for chat @-mentions.</summary>
public static class MentionPermissions
{
    private static readonly short[] SeniorStaffRoleBits =
    [
        PlatformRoles.SeniorTutor,
        PlatformRoles.HeadTutor,
        PlatformRoles.Moderator,
        PlatformRoles.SeniorModerator,
        PlatformRoles.CommunityManager,
        PlatformRoles.Administrator,
        PlatformRoles.SystemAdministrator,
        PlatformRoles.BoardMember,
        PlatformRoles.Owner,
        PlatformRoles.Founder,
    ];

    public static bool IsGuest(System.Collections.BitArray roleMask) =>
        BitMask.HasBit(roleMask, PlatformRoles.Guest)
        && !HasAnyRoleAboveGuest(roleMask);

    public static bool IsSeniorStaff(System.Collections.BitArray roleMask) =>
        SeniorStaffRoleBits.Any(bit => BitMask.HasBit(roleMask, bit));

    public static bool CanUseBroadcastMentions(System.Collections.BitArray roleMask) =>
        BitMask.HasBit(roleMask, PlatformRoles.Owner)
        || BitMask.HasBit(roleMask, PlatformRoles.Administrator);

    private static bool HasAnyRoleAboveGuest(System.Collections.BitArray roleMask)
    {
        for (short bit = PlatformRoles.VerifiedUser; bit <= PlatformRoles.TrialTutor; bit++)
        {
            if (bit < roleMask.Length && roleMask[bit])
                return true;
        }

        return false;
    }
}
