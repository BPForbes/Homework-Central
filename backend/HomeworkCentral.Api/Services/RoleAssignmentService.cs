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
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out short targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        (System.Collections.BitArray roles, System.Collections.BitArray moderation) granterMask =
            await GetExpandedRoleMaskAsync(granterUserId, ct);
        if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
            throw new UnauthorizedAccessException("You do not have permission to grant roles.");

        short granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
        if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
            throw new UnauthorizedAccessException("You can only grant roles below your own level.");

        Role role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, ct)
            ?? throw new InvalidOperationException($"Role '{roleName}' is not configured.");

        User targetUser = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
            ?? throw new InvalidOperationException("Target user was not found.");

        bool guestRemoved = false;
        if (string.Equals(roleName, "VerifiedUser", StringComparison.OrdinalIgnoreCase))
            guestRemoved = await RemoveRoleAsync(targetUser, "Guest", ct);

        if (targetUser.UserRoles.Any(ur => ur.RoleId == role.RoleId))
        {
            if (!guestRemoved)
                return;

            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction cleanupTransaction =
                await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await effectiveMaskService.RebuildUserEffectiveMaskAsync(targetUserId, ct);
            await cleanupTransaction.CommitAsync(ct);
            return;
        }

        db.UserRoles.Add(new UserRole
        {
            UserId = targetUserId,
            RoleId = role.RoleId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = granterUserId,
        });

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(targetUserId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RevokeRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out short targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        (System.Collections.BitArray roles, System.Collections.BitArray moderation) granterMask =
            await GetExpandedRoleMaskAsync(granterUserId, ct);
        if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
            throw new UnauthorizedAccessException("You do not have permission to revoke roles.");

        short granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
        if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
            throw new UnauthorizedAccessException("You can only revoke roles below your own level.");

        User targetUser = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
            ?? throw new InvalidOperationException("Target user was not found.");

        UserRole? assignment = targetUser.UserRoles.FirstOrDefault(ur =>
            string.Equals(ur.Role.Name, roleName, StringComparison.OrdinalIgnoreCase));

        if (assignment is null)
            return;

        db.UserRoles.Remove(assignment);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(targetUserId, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<bool> RemoveRoleAsync(User user, string roleName, CancellationToken ct)
    {
        UserRole? assignment = await db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur => ur.UserId == user.UserId && ur.Role.Name == roleName, ct);

        if (assignment is null)
            return false;

        db.UserRoles.Remove(assignment);
        return true;
    }

    private async Task<(System.Collections.BitArray roles, System.Collections.BitArray moderation)> GetExpandedRoleMaskAsync(
        Guid userId,
        CancellationToken ct)
    {
        UserEffectiveMask? effective = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct);
        if (effective is not null)
            return (effective.EffectiveRoleMask, effective.EffectiveModerationMask);

        User user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new InvalidOperationException("Granter user was not found.");

        System.Collections.BitArray roleMask = BitMask.Create(64);
        System.Collections.BitArray moderationMask = BitMask.Create(256);

        foreach (UserRole userRole in user.UserRoles)
        {
            roleMask = BitMask.Or(roleMask, userRole.Role.RoleMask);
            moderationMask = BitMask.Or(moderationMask, userRole.Role.PermissionMask);
        }

        return (roleMaskService.ExpandRoleIdentityMask(roleMask), moderationMask);
    }
}
