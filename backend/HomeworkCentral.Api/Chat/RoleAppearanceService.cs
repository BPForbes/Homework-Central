using System.Collections;
using System.Text.RegularExpressions;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Chat;

public interface IRoleAppearanceService
{
    Task<string> ResolveSenderColorAsync(BitArray roleMask, CancellationToken ct = default);
    Task<IReadOnlyList<MentionRoleOptionDto>> GetMentionableRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoleAppearanceDto>> ListRoleAppearanceAsync(CancellationToken ct = default);
    Task<RoleAppearanceDto?> UpdateRoleAppearanceAsync(Guid roleId, UpdateRoleAppearanceRequest request, CancellationToken ct = default);
    Task<bool> IsMentionablePlatformRoleAsync(string roleName, CancellationToken ct = default);
    Task<Guid?> TryGetMentionableCustomRoleIdAsync(string roleName, CancellationToken ct = default);
    Task PropagateCustomRoleAppearanceAsync(Guid roleId, CancellationToken ct = default);
}

public sealed partial class RoleAppearanceService(
    AppDbContext db,
    IAccessScopeAccessor accessScope,
    MasterDbContext masterRegistry,
    ITenantDbContextFactory tenantFactory) : IRoleAppearanceService
{
    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    public async Task<string> ResolveSenderColorAsync(BitArray roleMask, CancellationToken ct = default)
    {
        short highestBit = PlatformRoleCatalog.GetHighestRoleBit(roleMask);
        if (!PlatformRoleCatalog.TryGetRoleNameFromBit(highestBit, out string roleName))
            return RoleAppearanceDefaults.FallbackColor;

        Role? role = await db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => !r.IsCustom && r.Name == roleName, ct);

        return RoleAppearanceDefaults.ResolvePlatformRoleColor(roleName, role?.MessageColor);
    }

    public async Task<IReadOnlyList<MentionRoleOptionDto>> GetMentionableRolesAsync(CancellationToken ct = default)
    {
        AccountClass scope = accessScope.ResolveDbContextScope().AccountClass;
        List<Role> roles = await db.Roles
            .AsNoTracking()
            .Where(role => role.IsMentionableByUsers && role.OwnerAccountClass == scope)
            .OrderBy(role => role.Name)
            .ToListAsync(ct);

        return roles
            .Select(role => new MentionRoleOptionDto
            {
                Name = role.Name,
                MessageColor = role.IsCustom
                    ? RoleAppearanceDefaults.ResolveCustomRoleColor(role.MessageColor)
                    : RoleAppearanceDefaults.ResolvePlatformRoleColor(role.Name, role.MessageColor),
                IsCustom = role.IsCustom,
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<RoleAppearanceDto>> ListRoleAppearanceAsync(CancellationToken ct = default)
    {
        AccountClass scope = accessScope.ResolveDbContextScope().AccountClass;
        List<Role> roles = await db.Roles
            .AsNoTracking()
            .Where(role => role.OwnerAccountClass == scope)
            .OrderByDescending(role => role.IsCustom)
            .ThenBy(role => role.Name)
            .ToListAsync(ct);

        return roles.Select(MapAppearance).ToArray();
    }

    public async Task<RoleAppearanceDto?> UpdateRoleAppearanceAsync(
        Guid roleId,
        UpdateRoleAppearanceRequest request,
        CancellationToken ct = default)
    {
        AccountClass scope = accessScope.ResolveDbContextScope().AccountClass;
        Role? role = await db.Roles
            .FirstOrDefaultAsync(r => r.RoleId == roleId && r.OwnerAccountClass == scope, ct);

        if (role is null)
            return null;

        if (request.MessageColor is not null)
        {
            if (!HexColorRegex().IsMatch(request.MessageColor))
                throw new InvalidOperationException("Message color must be a hex value like #RRGGBB.");

            role.MessageColor = request.MessageColor;
        }

        if (request.IsMentionableByUsers is bool mentionable)
            role.IsMentionableByUsers = mentionable;

        await db.SaveChangesAsync(ct);

        if (role.IsCustom)
            await PropagateCustomRoleAppearanceAsync(role.RoleId, ct);

        return MapAppearance(role);
    }

    public async Task<bool> IsMentionablePlatformRoleAsync(string roleName, CancellationToken ct = default)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out _))
            return false;

        AccountClass scope = accessScope.ResolveDbContextScope().AccountClass;
        return await db.Roles
            .AsNoTracking()
            .AnyAsync(
                role => !role.IsCustom
                    && role.Name == roleName
                    && role.OwnerAccountClass == scope
                    && role.IsMentionableByUsers,
                ct);
    }

    public async Task<Guid?> TryGetMentionableCustomRoleIdAsync(string roleName, CancellationToken ct = default)
    {
        AccountClass scope = accessScope.ResolveDbContextScope().AccountClass;
        Role? role = await db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.IsCustom
                    && r.Name == roleName
                    && r.OwnerAccountClass == scope
                    && r.IsMentionableByUsers,
                ct);

        return role?.RoleId;
    }

    public async Task PropagateCustomRoleAppearanceAsync(Guid roleId, CancellationToken ct = default)
    {
        Role masterRole = await db.Roles
            .AsNoTracking()
            .FirstAsync(r => r.RoleId == roleId && r.IsCustom, ct);

        List<string> tenantDatabases = await masterRegistry.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.DatabaseName)
            .ToListAsync(ct);

        foreach (string databaseName in tenantDatabases)
        {
            await using AppDbContext tenantDb =
                await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);

            Role? tenantRole = await tenantDb.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct);
            if (tenantRole is null)
                continue;

            tenantRole.MessageColor = masterRole.MessageColor;
            tenantRole.IsMentionableByUsers = masterRole.IsMentionableByUsers;
            await tenantDb.SaveChangesAsync(ct);
        }
    }

    private static RoleAppearanceDto MapAppearance(Role role) =>
        new()
        {
            RoleId = role.RoleId,
            Name = role.Name,
            IsCustom = role.IsCustom,
            MessageColor = role.IsCustom
                ? RoleAppearanceDefaults.ResolveCustomRoleColor(role.MessageColor)
                : RoleAppearanceDefaults.ResolvePlatformRoleColor(role.Name, role.MessageColor),
            IsMentionableByUsers = role.IsMentionableByUsers,
        };
}
