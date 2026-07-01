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

        if (AuthorizationCatalog.TryGetRole(role.Name, out AuthorizationCatalog.RoleDefinition roleDefinition))
        {
            RoleMaskBuilder.RoleMaskSet masks = AuthorizationCatalog.GetRoleMasks(roleDefinition.Name);
            role.RoleMask = (BitArray)masks.RoleMask.Clone();
            role.PermissionMask = (BitArray)masks.PermissionMask.Clone();
            role.FeatureMask = (BitArray)masks.FeatureMask.Clone();
        }
        else
        {
            role.PermissionMask = RoleMaskBuilder.BuildPermissionMask(
                role.RolePermissions.Select(rp => rp.PermissionId));
            role.RoleMask = RoleMaskBuilder.BuildRoleIdentityMask(role.Name);
            role.FeatureMask = RoleMaskBuilder.BuildFeatureMask(role.Name);
        }

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
}
