using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public interface IRoleMaskService
{
    Task RebuildRoleMasksAsync(Guid roleId, CancellationToken ct = default);
    Task RebuildAllRoleMasksAsync(CancellationToken ct = default);
    BitArray ExpandRoleIdentityMask(BitArray roleMask);
}

public class RoleMaskService(AppDbContext db) : IRoleMaskService
{
    public async Task RebuildAllRoleMasksAsync(CancellationToken ct = default)
    {
        var roleIds = await db.Roles.Select(r => r.RoleId).ToListAsync(ct);
        foreach (var roleId in roleIds)
            await RebuildRoleMasksAsync(roleId, ct);
    }

    public async Task RebuildRoleMasksAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, ct);

        if (role is null)
            return;

        role.PermissionMask = BuildPermissionMask(role.RolePermissions);
        role.RoleMask = BuildRoleIdentityMask(role.Name);
        role.FeatureMask = BuildFeatureMask(role.Name);

        await db.SaveChangesAsync(ct);
    }

    public BitArray ExpandRoleIdentityMask(BitArray roleMask)
    {
        var expanded = (BitArray)roleMask.Clone();
        for (var bit = 0; bit < roleMask.Length; bit++)
        {
            if (!roleMask[bit])
                continue;

            foreach (var inherited in RoleHierarchy.ExpandRoleBits((short)bit))
            {
                if (inherited != bit)
                    BitMask.SetBit(expanded, inherited);
            }
        }

        return expanded;
    }

    private static BitArray BuildPermissionMask(IEnumerable<RolePermission> rolePermissions)
    {
        var mask = BitMask.Create(256);
        foreach (var rp in rolePermissions)
            BitMask.SetBit(mask, rp.PermissionId);
        return mask;
    }

    private static BitArray BuildRoleIdentityMask(string roleName) =>
        TryGetRoleBit(roleName, out var bit)
            ? SetSingleBit(64, bit)
            : BitMask.Create(64);

    private static BitArray BuildFeatureMask(string roleName)
    {
        var mask = BitMask.Create(256);

        switch (roleName)
        {
            case "Guest":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PublicProfile);
                break;
            case "VerifiedUser":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads);
                break;
            case "Student":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.ProfileCustomization,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads,
                    PlatformFeatures.EventCalendar);
                break;
            case "Tutor":
            case "SeniorTutor":
            case "HeadTutor":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.VoiceRooms,
                    PlatformFeatures.VideoRooms,
                    PlatformFeatures.ScreenSharing,
                    PlatformFeatures.SeminarHosting,
                    PlatformFeatures.SeminarUpload,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.ProfileCustomization,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads,
                    PlatformFeatures.EventCalendar);
                break;
            case "SeminarHost":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.SeminarHosting,
                    PlatformFeatures.SeminarUpload,
                    PlatformFeatures.ScreenSharing,
                    PlatformFeatures.PublicProfile);
                break;
            case "Moderator":
            case "SeniorModerator":
            case "CommunityManager":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.WikiEditing,
                    PlatformFeatures.CommunityAnnouncements,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.AnalyticsDashboard);
                break;
            case "EventOrganizer":
                SetFeatures(mask,
                    PlatformFeatures.EventPosting,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.CommunityPolls,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.GroupMessages);
                break;
            case "BetaTester":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads,
                    PlatformFeatures.BetaFeatures);
                break;
            case "Administrator":
            case "SystemAdministrator":
            case "Owner":
                EnableAllFeatures(mask);
                break;
            default:
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PublicProfile);
                break;
        }

        return mask;
    }

    private static void EnableAllFeatures(BitArray mask)
    {
        for (var i = 0; i <= PlatformFeatures.BetaFeatures; i++)
            BitMask.SetBit(mask, i);
    }

    private static void SetFeatures(BitArray mask, params short[] features)
    {
        foreach (var feature in features)
            BitMask.SetBit(mask, feature);
    }

    private static bool TryGetRoleBit(string roleName, out short bit)
    {
        bit = roleName switch
        {
            "Guest" => PlatformRoles.Guest,
            "VerifiedUser" => PlatformRoles.VerifiedUser,
            "Student" => PlatformRoles.Student,
            "Tutor" => PlatformRoles.Tutor,
            "SeniorTutor" => PlatformRoles.SeniorTutor,
            "HeadTutor" => PlatformRoles.HeadTutor,
            "Moderator" => PlatformRoles.Moderator,
            "SeniorModerator" => PlatformRoles.SeniorModerator,
            "CommunityManager" => PlatformRoles.CommunityManager,
            "EventOrganizer" => PlatformRoles.EventOrganizer,
            "SeminarHost" => PlatformRoles.SeminarHost,
            "VerifiedEducator" => PlatformRoles.VerifiedEducator,
            "Developer" => PlatformRoles.Developer,
            "Administrator" => PlatformRoles.Administrator,
            "SystemAdministrator" => PlatformRoles.SystemAdministrator,
            "Owner" => PlatformRoles.Owner,
            "Founder" => PlatformRoles.Founder,
            "BoardMember" => PlatformRoles.BoardMember,
            "BetaTester" => PlatformRoles.BetaTester,
            "Staff" => PlatformRoles.Staff,
            _ => -1,
        };

        return bit >= 0;
    }

    private static BitArray SetSingleBit(int length, short bit)
    {
        var mask = BitMask.Create(length);
        BitMask.SetBit(mask, bit);
        return mask;
    }
}
