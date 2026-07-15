using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public interface IRoleAssignmentService
{
    Task AssignRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default);
    Task RevokeRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default);
}

/// <summary>
/// Tenant-aware, mirroring <see cref="AuthService"/>/<see cref="HomeworkCentral.Api.Captcha.CaptchaRoleService"/>'s
/// DB resolution: a dev persona's <c>Users</c> row lives only in its own tenant database, so
/// granting/revoking a role against the injected master <see cref="AppDbContext"/> would violate
/// the <c>UserRoles.UserId</c>/<c>AssignedBy</c> foreign keys against <c>master.Users</c> whenever
/// the granter or target is a tenant/persona account.
/// </summary>
public class RoleAssignmentService(
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
    IHttpContextAccessor httpContextAccessor,
    IEffectiveMaskService effectiveMaskService,
    IRoleMaskService roleMaskService) : IRoleAssignmentService
{
    public async Task AssignRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetCanonicalRoleName(roleName, out string canonicalRoleName, out short targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            (System.Collections.BitArray roles, System.Collections.BitArray moderation) granterMask =
                await GetExpandedRoleMaskAsync(db, granterUserId, ct);
            if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
                throw new UnauthorizedAccessException("You do not have permission to grant roles.");

            short granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
            if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
                throw new UnauthorizedAccessException("You can only grant roles below your own level.");

            Role role = await db.Roles.FirstOrDefaultAsync(r => r.Name == canonicalRoleName, ct)
                ?? throw new InvalidOperationException($"Role '{canonicalRoleName}' is not configured.");

            User targetUser = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
                ?? throw new InvalidOperationException("Target user was not found.");
            short targetLevel = InfrastructureUserDirectory.GetHighestPlatformRoleBit(targetUser);
            if (targetUserId == granterUserId || !PlatformRoleCatalog.CanGrantRole(granterLevel, targetLevel))
                throw new UnauthorizedAccessException("You can only manage users below your own level.");

            bool conflictingRoleRemoved = false;
            if (string.Equals(canonicalRoleName, "VerifiedUser", StringComparison.Ordinal))
                conflictingRoleRemoved = await RemoveRoleAsync(db, targetUser, "Guest", ct);
            else if (string.Equals(canonicalRoleName, "Guest", StringComparison.Ordinal))
                conflictingRoleRemoved = await RemoveRoleAsync(db, targetUser, "VerifiedUser", ct);

            if (targetUser.UserRoles.Any(ur => ur.RoleId == role.RoleId))
            {
                if (!conflictingRoleRemoved)
                    return;

                await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction cleanupTransaction =
                    await db.Database.BeginTransactionAsync(ct);
                await db.SaveChangesAsync(ct);
                await EffectiveMaskService.RebuildOnContextAsync(db, targetUserId, ct);
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
            await EffectiveMaskService.RebuildOnContextAsync(db, targetUserId, ct);
            await transaction.CommitAsync(ct);
        }
        finally
        {
            if (disposeDb)
                await db.DisposeAsync();
        }
    }

    public async Task RevokeRoleAsync(Guid granterUserId, Guid targetUserId, string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetCanonicalRoleName(roleName, out string canonicalRoleName, out short targetRoleBit))
            throw new InvalidOperationException($"Unknown role '{roleName}'.");

        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            (System.Collections.BitArray roles, System.Collections.BitArray moderation) granterMask =
                await GetExpandedRoleMaskAsync(db, granterUserId, ct);
            if (!BitMask.HasBit(granterMask.moderation, ModerationPermissions.ManageRoles))
                throw new UnauthorizedAccessException("You do not have permission to revoke roles.");

            short granterLevel = PlatformRoleCatalog.GetHighestRoleBit(granterMask.roles);
            if (!PlatformRoleCatalog.CanGrantRole(granterLevel, targetRoleBit))
                throw new UnauthorizedAccessException("You can only revoke roles below your own level.");

            User targetUser = await db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
                ?? throw new InvalidOperationException("Target user was not found.");
            short targetLevel = InfrastructureUserDirectory.GetHighestPlatformRoleBit(targetUser);
            if (targetUserId == granterUserId || !PlatformRoleCatalog.CanGrantRole(granterLevel, targetLevel))
                throw new UnauthorizedAccessException("You can only manage users below your own level.");

            UserRole? assignment = targetUser.UserRoles.FirstOrDefault(ur =>
                string.Equals(ur.Role.Name, canonicalRoleName, StringComparison.Ordinal));

            if (assignment is null)
                return;

            db.UserRoles.Remove(assignment);
            if (string.Equals(canonicalRoleName, "VerifiedUser", StringComparison.Ordinal)
                && !targetUser.UserRoles.Any(ur => string.Equals(ur.Role.Name, "Guest", StringComparison.Ordinal)))
            {
                Role guest = await db.Roles.FirstAsync(r => r.Name == "Guest", ct);
                db.UserRoles.Add(new UserRole
                {
                    UserId = targetUserId,
                    RoleId = guest.RoleId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = granterUserId,
                });
            }
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await EffectiveMaskService.RebuildOnContextAsync(db, targetUserId, ct);
            await transaction.CommitAsync(ct);
        }
        finally
        {
            if (disposeDb)
                await db.DisposeAsync();
        }
    }

    private async Task<AppDbContext> ResolveDbContextAsync(CancellationToken ct)
    {
        string? tenantDatabaseName = httpContextAccessor.HttpContext?.User
            .FindFirst(TenancyConstants.TenantDbClaimName)?.Value;

        return string.IsNullOrEmpty(tenantDatabaseName)
            ? masterDb
            : await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabaseName, ct);
    }

    private static async Task<bool> RemoveRoleAsync(AppDbContext db, User user, string roleName, CancellationToken ct)
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
        AppDbContext db,
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

        Dictionary<short, Role> rolesByBit = await BuildRolesByBitAsync(db, ct);

        System.Collections.BitArray roleMask = BitMask.Create(64);
        System.Collections.BitArray moderationMask = BitMask.Create(256);

        foreach (UserRole userRole in user.UserRoles)
        {
            if (!PlatformRoleCatalog.TryGetRoleBit(userRole.Role.Name, out short directBit))
                continue;

            foreach (short bit in RoleHierarchy.ExpandRoleBits(directBit))
            {
                if (!rolesByBit.TryGetValue(bit, out Role? inheritedRole))
                    continue;

                BitMask.SetBit(roleMask, bit);
                moderationMask = BitMask.Or(moderationMask, inheritedRole.PermissionMask);
            }
        }

        roleMask = roleMaskService.ExpandRoleIdentityMask(roleMask);
        return (roleMask, moderationMask);
    }

    private static async Task<Dictionary<short, Role>> BuildRolesByBitAsync(AppDbContext db, CancellationToken ct)
    {
        Dictionary<short, Role> rolesByBit = new();
        List<Role> roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        foreach (Role role in roles)
        {
            if (PlatformRoleCatalog.TryGetRoleBit(role.Name, out short bit))
                rolesByBit[bit] = role;
        }

        return rolesByBit;
    }
}
