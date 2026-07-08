using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

/// <summary>
/// Custom roles are authored on the master database but must exist in tenant databases
/// before a persona can claim or be assigned one (UserRoles FK + effective-mask rebuild).
/// </summary>
public static class CustomRoleReplicationService
{
    public static async Task EnsureRoleSyncedAsync(
        AppDbContext masterDb,
        AppDbContext targetDb,
        Guid roleId,
        CancellationToken ct = default)
    {
        Role masterRole = await masterDb.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .FirstAsync(r => r.RoleId == roleId && r.IsCustom, ct);

        Role? targetRole = await targetDb.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, ct);

        if (targetRole is null)
        {
            targetDb.Roles.Add(CloneRole(masterRole));
            await targetDb.SaveChangesAsync(ct);
            return;
        }

        ApplyMasterFields(targetRole, masterRole);
        SyncPermissions(targetRole, masterRole);
        await targetDb.SaveChangesAsync(ct);
    }

    private static Role CloneRole(Role masterRole)
    {
        Role clone = new()
        {
            RoleId = masterRole.RoleId,
            Name = masterRole.Name,
            Description = masterRole.Description,
            IsCustom = true,
            CreatedAtUtc = masterRole.CreatedAtUtc,
            OwnerAccountClass = masterRole.OwnerAccountClass,
            ClaimHostRoomId = masterRole.ClaimHostRoomId,
            IconName = masterRole.IconName,
            MessageColor = masterRole.MessageColor,
            IsMentionableByUsers = masterRole.IsMentionableByUsers,
            RoleMask = (System.Collections.BitArray)masterRole.RoleMask.Clone(),
            PermissionMask = (System.Collections.BitArray)masterRole.PermissionMask.Clone(),
            FeatureMask = (System.Collections.BitArray)masterRole.FeatureMask.Clone(),
        };

        foreach (RolePermission permission in masterRole.RolePermissions)
        {
            clone.RolePermissions.Add(new RolePermission
            {
                RoleId = clone.RoleId,
                PermissionId = permission.PermissionId,
            });
        }

        return clone;
    }

    private static void ApplyMasterFields(Role targetRole, Role masterRole)
    {
        targetRole.Name = masterRole.Name;
        targetRole.Description = masterRole.Description;
        targetRole.IsCustom = true;
        targetRole.CreatedAtUtc = masterRole.CreatedAtUtc;
        targetRole.OwnerAccountClass = masterRole.OwnerAccountClass;
        targetRole.ClaimHostRoomId = masterRole.ClaimHostRoomId;
        targetRole.IconName = masterRole.IconName;
        targetRole.MessageColor = masterRole.MessageColor;
        targetRole.IsMentionableByUsers = masterRole.IsMentionableByUsers;
        targetRole.RoleMask = (System.Collections.BitArray)masterRole.RoleMask.Clone();
        targetRole.PermissionMask = (System.Collections.BitArray)masterRole.PermissionMask.Clone();
        targetRole.FeatureMask = (System.Collections.BitArray)masterRole.FeatureMask.Clone();
    }

    private static void SyncPermissions(Role targetRole, Role masterRole)
    {
        HashSet<short> desired = masterRole.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        List<RolePermission> toRemove = targetRole.RolePermissions
            .Where(rp => !desired.Contains(rp.PermissionId))
            .ToList();
        foreach (RolePermission permission in toRemove)
            targetRole.RolePermissions.Remove(permission);

        HashSet<short> existing = targetRole.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        foreach (short permissionId in desired.Where(id => !existing.Contains(id)))
        {
            targetRole.RolePermissions.Add(new RolePermission
            {
                RoleId = targetRole.RoleId,
                PermissionId = permissionId,
            });
        }
    }
}
