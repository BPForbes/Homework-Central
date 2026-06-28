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
        List<Guid> roleIds = await db.Roles.Select(r => r.RoleId).ToListAsync(ct);
        foreach (Guid roleId in roleIds)
            await RebuildRoleMasksAsync(roleId, ct);
    }

    public async Task RebuildRoleMasksAsync(Guid roleId, CancellationToken ct = default)
    {
        Role? role = await db.Roles
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
        BitArray expanded = (BitArray)roleMask.Clone();
        for (int bit = 0; bit < roleMask.Length; bit++)
        {
            if (!roleMask[bit])
                continue;

            foreach (short inherited in RoleHierarchy.ExpandRoleBits((short)bit))
            {
                if (inherited != bit)
                    BitMask.SetBit(expanded, inherited);
            }
        }

        return expanded;
    }

    private static BitArray BuildPermissionMask(IEnumerable<RolePermission> rolePermissions)
    {
        BitArray mask = BitMask.Create(256);
        foreach (RolePermission rp in rolePermissions)
            BitMask.SetBit(mask, rp.PermissionId);
        return mask;
    }

    private static BitArray BuildRoleIdentityMask(string roleName)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out short bit))
            throw new InvalidOperationException($"Role '{roleName}' is not defined in PlatformRoleCatalog.");

        return SetSingleBit(64, bit);
    }

    private static BitArray BuildFeatureMask(string roleName)
    {
        BitArray mask = BitMask.Create(256);

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
            case "Staff":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.EventCalendar);
                break;
            case "Tutor":
                SetFeatures(mask, TutorFeatures());
                break;
            case "SeniorTutor":
            case "HeadTutor":
                SetFeatures(mask, TutorFeatures());
                SetFeatures(mask,
                    PlatformFeatures.SeminarHosting,
                    PlatformFeatures.SeminarUpload,
                    PlatformFeatures.EventPosting,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.CommunityPolls);
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
            case "BoardMember":
            case "Owner":
            case "Founder":
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

    private static short[] TutorFeatures() =>
    [
        PlatformFeatures.PublicMessages,
        PlatformFeatures.PrivateMessages,
        PlatformFeatures.GroupMessages,
        PlatformFeatures.VoiceRooms,
        PlatformFeatures.VideoRooms,
        PlatformFeatures.ScreenSharing,
        PlatformFeatures.ResourceSharing,
        PlatformFeatures.PublicProfile,
        PlatformFeatures.ProfileCustomization,
        PlatformFeatures.FileUploads,
        PlatformFeatures.ImageUploads,
        PlatformFeatures.EventCalendar,
    ];

    private static void EnableAllFeatures(BitArray mask)
    {
        for (int i = 0; i <= PlatformFeatures.BetaFeatures; i++)
            BitMask.SetBit(mask, i);
    }

    private static void SetFeatures(BitArray mask, params short[] features)
    {
        foreach (short feature in features)
            BitMask.SetBit(mask, feature);
    }

    private static BitArray SetSingleBit(int length, short bit)
    {
        BitArray mask = BitMask.Create(length);
        BitMask.SetBit(mask, bit);
        return mask;
    }
}
