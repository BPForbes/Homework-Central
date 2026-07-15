using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Infrastructure;

public static class ModerationRiskPermissions
{
    public static readonly short[] HighRiskBits =
    [
        ModerationPermissions.WarnUser,
        ModerationPermissions.TimeoutUser,
        ModerationPermissions.MuteMembers,
        ModerationPermissions.KickUser,
        ModerationPermissions.BanMembers,
        ModerationPermissions.DeleteMessages,
        ModerationPermissions.EditMessages,
        ModerationPermissions.PinMessages,
        ModerationPermissions.LockChannels,
        ModerationPermissions.ManageChannels,
        ModerationPermissions.ManageRoles,
        ModerationPermissions.ManagePermissions,
        ModerationPermissions.SuspendAccounts,
        ModerationPermissions.ManageServerInfrastructure,
    ];

    public static bool RoleHasHighRiskPermissions(Role role)
    {
        foreach (short bit in HighRiskBits)
        {
            if (BitMask.HasBit(role.PermissionMask, bit))
                return true;
        }

        return false;
    }

    public static List<string> GetHighRiskPermissionNames(Role role)
    {
        List<string> names = new();
        foreach (AuthorizationCatalog.PermissionDefinition permission in AuthorizationCatalog.Permissions)
        {
            if (HighRiskBits.Contains(permission.PermissionId)
                && BitMask.HasBit(role.PermissionMask, permission.PermissionId))
            {
                names.Add(permission.Name);
            }
        }

        return names;
    }
}
