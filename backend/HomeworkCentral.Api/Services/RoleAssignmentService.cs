using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public interface IRoleAssignmentService
{
    Task AssignRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default);
    Task RevokeRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default);
}

public class RoleAssignmentService(
    AppDbContext db,
    IEffectiveMaskService effectiveMaskService,
    IRoleMaskService roleMaskService) : IRoleAssignmentService
{
    public async Task AssignRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out var targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        var granterMask = await GetExpandedRoleMaskAsync(granterUserId, ct);
        if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
            throw new UnauthorizedAccessException("You do not have permission to grant roles.");

        var granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
        if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
            throw new UnauthorizedAccessException("You can only grant roles below your own level.");

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct)
            ?? throw new InvalidOperationException($"Role '{roleName}' is not configured.");

        var targetUser = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
            ?? throw new InvalidOperationException("Target user was not found.");

        if (targetUser.UserRoles.Any(ur => ur.RoleId == role.RoleId))
            return;

        if (string.Equals(roleName, "VerifiedUser", StringComparison.OrdinalIgnoreCase))
            await RemoveRoleAsync(targetUser, "Guest", ct);

        db.UserRoles.Add(new UserRole
        {
            UserId = targetUserId,
            RoleId = role.RoleId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = granterUserId,
        });

        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(targetUserId, ct);
    }

    public async Task RevokeRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out var targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        var granterMask = await GetExpandedRoleMaskAsync(granterUserId, ct);
        if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
            throw new UnauthorizedAccessException("You do not have permission to revoke roles.");

        var granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
        if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
            throw new UnauthorizedAccessException("You can only revoke roles below your own level.");

        var targetUser = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
            ?? throw new InvalidOperationException("Target user was not found.");

        var assignment = targetUser.UserRoles.FirstOrDefault(ur =>
            string.Equals(ur.Role.Name, roleName, StringComparison.OrdinalIgnoreCase));

        if (assignment is null)
            return;

        db.UserRoles.Remove(assignment);
        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(targetUserId, ct);
    }

    private async Task RemoveRoleAsync(User user, string roleName, CancellationToken ct)
    {
        var assignment = await db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur => ur.UserId == user.UserId && ur.Role.Name == roleName, ct);

        if (assignment is not null)
            db.UserRoles.Remove(assignment);
    }

    private async Task<(System.Collections.BitArray roles, System.Collections.BitArray moderation)> GetExpandedRoleMaskAsync(
        Guid userId,
        CancellationToken ct)
    {
        var effective = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct);
        if (effective is not null)
            return (effective.EffectiveRoleMask, effective.EffectiveModerationMask);

        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new InvalidOperationException("Granter user was not found.");

        var roleMask = BitMask.Create(64);
        var moderationMask = BitMask.Create(256);

        foreach (var userRole in user.UserRoles)
        {
            roleMask = BitMask.Or(roleMask, userRole.Role.RoleMask);
            moderationMask = BitMask.Or(moderationMask, userRole.Role.PermissionMask);
        }

        return (roleMaskService.ExpandRoleIdentityMask(roleMask), moderationMask);
    }
}
