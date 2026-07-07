using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

public interface IInfrastructureService
{
    Task<IReadOnlyList<CustomRoleDto>> ListCustomRolesAsync(CancellationToken ct = default);
    Task<CustomRoleDto> CreateCustomRoleAsync(Guid actorUserId, CreateCustomRoleRequest request, CancellationToken ct = default);
    Task<CustomRoleDto?> UpdateCustomRoleAsync(Guid actorUserId, Guid roleId, UpdateCustomRoleRequest request, CancellationToken ct = default);
    Task<bool> SetRoleClaimPlacementAsync(Guid actorUserId, Guid roleId, SetRoleClaimPlacementRequest request, CancellationToken ct = default);
    Task<bool> DeleteCustomRoleAsync(Guid roleId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomChannelDto>> ListCustomChannelsAsync(Guid actorUserId, CancellationToken ct = default);
    Task<CustomChannelDto> CreateCustomChannelAsync(Guid actorUserId, CreateCustomChannelRequest request, CancellationToken ct = default);
    Task<CustomChannelDto?> UpdateCustomChannelAsync(Guid actorUserId, Guid channelId, UpdateCustomChannelRequest request, CancellationToken ct = default);
    Task<bool> ArchiveCustomChannelAsync(Guid channelId, CancellationToken ct = default);

    Task<IReadOnlyList<ClaimableCustomRoleDto>> GetClaimableRolesAsync(Guid userId, string roomId, CancellationToken ct = default);
    Task<bool> ClaimCustomRoleAsync(Guid userId, Guid roleId, string roomId, CancellationToken ct = default);
    Task<bool> UnclaimCustomRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    Task<CustomChannelDto?> GetChannelForUserAsync(Guid userId, string roomId, CancellationToken ct = default);
    Task<ModerationRiskWarningDto?> PreviewAccessRuleRiskAsync(Guid customRoleId, bool isPublicRoom, CancellationToken ct = default);

    Task<IReadOnlyList<InfrastructureUserLookupDto>> SearchUsersAsync(string query, CancellationToken ct = default);
    Task<InfrastructureUserLookupDto?> GetUserWithCustomRolesAsync(Guid userId, string? tenantDatabaseName = null, CancellationToken ct = default);
    Task<IReadOnlyList<AssignableUserDto>> ListAssignableUsersAsync(Guid actorUserId, Guid roleId, CancellationToken ct = default);
    Task<int> BulkAssignCustomRoleAsync(Guid actorUserId, Guid roleId, BulkAssignCustomRoleRequest request, CancellationToken ct = default);
    Task<bool> AdminAssignCustomRoleAsync(Guid actorUserId, Guid targetUserId, Guid roleId, string? tenantDatabaseName = null, CancellationToken ct = default);
    Task<bool> AdminRevokeCustomRoleAsync(Guid targetUserId, Guid roleId, string? tenantDatabaseName = null, CancellationToken ct = default);
}

public sealed class InfrastructureService(
    AppDbContext db,
    IRoleMaskService roleMaskService,
    IEffectiveMaskService effectiveMaskService,
    ICustomChannelStore channelStore,
    IPasswordConfirmationService passwordConfirmation,
    IChatRoomAccessService chatRoomAccess,
    IAccessScopeAccessor accessScope,
    IChatNavNotifier chatNavNotifier,
    InfrastructureUserDirectory userDirectory) : IInfrastructureService
{
    private const int InfoRoomAdminEditDays = 3;

    public async Task<IReadOnlyList<CustomRoleDto>> ListCustomRolesAsync(CancellationToken ct = default)
    {
        AccessScope? scope = RequireScope();
        List<Role> roles = await db.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .Where(r => r.IsCustom)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return roles
            .Where(role => InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            .Select(MapRole)
            .ToList();
    }

    public async Task<CustomRoleDto> CreateCustomRoleAsync(
        Guid actorUserId,
        CreateCustomRoleRequest request,
        CancellationToken ct = default)
    {
        string name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Role name is required.");

        if (AuthorizationCatalog.RolesByName.ContainsKey(name)
            || PlatformRoleCatalog.TryGetRoleBit(name, out _))
        {
            throw new InvalidOperationException("That role name conflicts with a built-in platform role.");
        }

        bool exists = await db.Roles.AnyAsync(r => r.Name == name, ct);
        if (exists)
            throw new InvalidOperationException("A role with that name already exists.");

        DateTime now = DateTime.UtcNow;
        AccessScope scope = RequireScope();
        Role role = new Role
        {
            RoleId = Guid.NewGuid(),
            Name = name,
            Description = request.Description?.Trim(),
            IsCustom = true,
            CreatedAtUtc = now,
            ClaimHostRoomId = null,
            IconName = NormalizeIconName(request.IconName),
            OwnerAccountClass = scope.AccountClass,
        };

        foreach (short permissionId in request.PermissionIds.Distinct())
        {
            if (!AuthorizationCatalog.Permissions.Any(p => p.PermissionId == permissionId))
                throw new InvalidOperationException($"Unknown permission id {permissionId}.");

            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.RoleId,
                PermissionId = permissionId,
            });
        }

        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        await roleMaskService.RebuildRoleMasksAsync(role.RoleId, ct);
        await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);

        return MapRole(await db.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .FirstAsync(r => r.RoleId == role.RoleId, ct));
    }

    public async Task<CustomRoleDto?> UpdateCustomRoleAsync(
        Guid actorUserId,
        Guid roleId,
        UpdateCustomRoleRequest request,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);

        if (role is null)
            return null;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return null;

        if (request.Name is not null)
        {
            string name = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Role name is required.");

            if (AuthorizationCatalog.RolesByName.ContainsKey(name)
                || PlatformRoleCatalog.TryGetRoleBit(name, out _))
            {
                throw new InvalidOperationException("That role name conflicts with a built-in platform role.");
            }

            bool nameTaken = await db.Roles.AnyAsync(r => r.Name == name && r.RoleId != roleId, ct);
            if (nameTaken)
                throw new InvalidOperationException("A role with that name already exists.");

            role.Name = name;
        }

        if (request.Description is not null)
            role.Description = request.Description.Trim();

        if (request.IconName is not null)
            role.IconName = NormalizeIconName(request.IconName);

        if (request.PermissionIds is not null)
        {
            HashSet<short> desired = request.PermissionIds.Distinct().ToHashSet();
            List<RolePermission> toRemove = role.RolePermissions
                .Where(rp => !desired.Contains(rp.PermissionId))
                .ToList();
            foreach (RolePermission rp in toRemove)
                role.RolePermissions.Remove(rp);

            HashSet<short> existing = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (short permissionId in desired.Where(id => !existing.Contains(id)))
            {
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    PermissionId = permissionId,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await roleMaskService.RebuildRoleMasksAsync(role.RoleId, ct);

        List<Guid> affectedUsers = await db.UserRoles
            .Where(ur => ur.RoleId == role.RoleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        foreach (Guid userId in affectedUsers)
            await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);

        await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);

        return MapRole(await db.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .FirstAsync(r => r.RoleId == role.RoleId, ct));
    }

    public async Task<bool> SetRoleClaimPlacementAsync(
        Guid actorUserId,
        Guid roleId,
        SetRoleClaimPlacementRequest request,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            return false;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return false;

        string? hostRoomId = string.IsNullOrWhiteSpace(request.ClaimHostRoomId)
            ? null
            : request.ClaimHostRoomId.Trim();

        if (hostRoomId is not null
            && !string.Equals(hostRoomId, InfrastructureRoleClaimRooms.DefaultClaimRoomId, StringComparison.Ordinal))
        {
            CustomChannelSnapshot? hostRoom = channelStore.FindByRoomId(hostRoomId);
            if (hostRoom is null)
                throw new InvalidOperationException("Claim host room was not found.");

            if (!InfrastructureAccountScope.CanViewInfrastructure(scope, hostRoom.OwnerAccountClass))
                throw new InvalidOperationException("Claim host room is not available in your account scope.");
        }

        if (hostRoomId is not null
            && await RoleClaimCycleValidator.PlacementWouldBeSelfReferentialAsync(db, roleId, hostRoomId, ct))
        {
            throw new InvalidOperationException(
                "That placement would make the role-claim room self-referential: members need this role to enter the same room where it is claimed.");
        }

        role.ClaimHostRoomId = hostRoomId;
        await db.SaveChangesAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);
        return true;
    }

    public async Task<bool> DeleteCustomRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        Role? role = await db.Roles
            .Include(r => r.UserRoles)
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);

        if (role is null)
            return false;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return false;

        List<CustomChannelAccessRule> accessRules = await db.CustomChannelAccessRules
            .Where(r => r.CustomRoleId == roleId)
            .ToListAsync(ct);
        db.CustomChannelAccessRules.RemoveRange(accessRules);
        db.UserRoles.RemoveRange(role.UserRoles);
        db.RolePermissions.RemoveRange(role.RolePermissions);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);
        return true;
    }

    public async Task<IReadOnlyList<CustomChannelDto>> ListCustomChannelsAsync(Guid actorUserId, CancellationToken ct = default)
    {
        AccessScope scope = RequireScope();
        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);

        List<CustomChannel> channels = await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .ThenInclude(r => r.CustomRole)
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.CategoryDisplayName)
            .ThenBy(c => c.DisplayName)
            .ToListAsync(ct);

        return channels
            .Where(channel => InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass))
            .Select(c => MapChannel(c, mask))
            .ToList();
    }

    public async Task<CustomChannelDto> CreateCustomChannelAsync(
        Guid actorUserId,
        CreateCustomChannelRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse(request.RoomType, ignoreCase: true, out CustomRoomType roomType))
            throw new InvalidOperationException("Invalid room type.");

        if (!Enum.TryParse(request.TieType, ignoreCase: true, out ChannelTieType tieType))
            throw new InvalidOperationException("Invalid tie type.");

        Guid channelId = Guid.NewGuid();
        string roomId = CustomChannelIds.BuildRoomId(channelId);
        DateTime now = DateTime.UtcNow;
        AccessScope scope = RequireScope();

        (string categoryKey, string categoryDisplayName) = NormalizeCategory(
            request.CategoryKey,
            request.CategoryDisplayName);

        CustomChannel channel = new CustomChannel
        {
            ChannelId = channelId,
            RoomId = roomId,
            DisplayName = request.DisplayName.Trim(),
            IconName = NormalizeIconName(request.IconName),
            CategoryKey = categoryKey,
            CategoryDisplayName = categoryDisplayName,
            RoomType = roomType,
            IsPrivate = request.IsPrivate,
            InfoContent = request.InfoContent,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            OwnerAccountClass = scope.AccountClass,
            TieType = tieType,
            TieSubjectMask = request.TieSubjectMask,
            TieSubjectBitIndex = request.TieSubjectBitIndex,
            TiePlatformRoleBit = request.TiePlatformRoleBit,
        };

        await ApplyAccessRulesAsync(channel, request.AccessRules, request.IsPrivate, request.Password, actorUserId, ct);

        if (roomType == CustomRoomType.RoleClaim
            && channel.IsPrivate
            && await RoleClaimCycleValidator.WouldBeSelfReferentialAsync(
                db,
                roomId,
                channel.AccessRules.Where(r => r.CustomRoleId.HasValue).Select(r => r.CustomRoleId!.Value),
                ct))
        {
            throw new InvalidOperationException(
                "This role-claim room would be self-referential: a required access role can only be claimed inside this same room.");
        }

        db.CustomChannels.Add(channel);
        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(channel.OwnerAccountClass, ct);

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);

        return MapChannel(await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .ThenInclude(r => r.CustomRole)
            .FirstAsync(c => c.ChannelId == channel.ChannelId, ct), mask);
    }

    public async Task<CustomChannelDto?> UpdateCustomChannelAsync(
        Guid actorUserId,
        Guid channelId,
        UpdateCustomChannelRequest request,
        CancellationToken ct = default)
    {
        CustomChannel? channel = await db.CustomChannels
            .Include(c => c.AccessRules)
            .ThenInclude(r => r.CustomRole)
            .FirstOrDefaultAsync(c => c.ChannelId == channelId && !c.IsArchived, ct);

        if (channel is null)
            return null;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass))
            return null;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);

        if (request.InfoContent is not null && !CanEditInfoRoom(mask, channel))
        {
            throw new InvalidOperationException(
                "You cannot edit this info room. Administrators may only edit info rooms within three days of creation; Owner and System Administrator can always edit.");
        }

        if (request.DisplayName is not null)
            channel.DisplayName = request.DisplayName.Trim();
        if (request.IconName is not null)
            channel.IconName = NormalizeIconName(request.IconName);
        if (request.CategoryKey is not null || request.CategoryDisplayName is not null)
        {
            (string categoryKey, string categoryDisplayName) = NormalizeCategory(
                request.CategoryKey ?? channel.CategoryKey,
                request.CategoryDisplayName ?? channel.CategoryDisplayName);
            channel.CategoryKey = categoryKey;
            channel.CategoryDisplayName = categoryDisplayName;
        }
        bool wasPrivate = channel.IsPrivate;
        if (request.IsPrivate.HasValue)
            channel.IsPrivate = request.IsPrivate.Value;
        if (request.InfoContent is not null)
            channel.InfoContent = request.InfoContent;
        if (request.TieType is not null && Enum.TryParse(request.TieType, ignoreCase: true, out ChannelTieType tieType))
            channel.TieType = tieType;
        if (request.TieSubjectMask is not null)
            channel.TieSubjectMask = request.TieSubjectMask;
        if (request.TieSubjectBitIndex.HasValue)
            channel.TieSubjectBitIndex = request.TieSubjectBitIndex;
        if (request.TiePlatformRoleBit.HasValue)
            channel.TiePlatformRoleBit = request.TiePlatformRoleBit;

        bool becomingPublic = request.IsPrivate.HasValue && wasPrivate && !channel.IsPrivate;
        bool becomingPrivate = request.IsPrivate.HasValue && !wasPrivate && channel.IsPrivate;

        if (becomingPublic)
        {
            db.CustomChannelAccessRules.RemoveRange(channel.AccessRules);
            channel.AccessRules.Clear();
        }
        else if (request.AccessRules is not null)
        {
            db.CustomChannelAccessRules.RemoveRange(channel.AccessRules);
            channel.AccessRules.Clear();
            await ApplyAccessRulesAsync(
                channel,
                request.AccessRules,
                channel.IsPrivate,
                request.Password,
                actorUserId,
                ct);
        }
        else if (becomingPrivate)
        {
            throw new InvalidOperationException("Private rooms must specify at least one access role.");
        }

        if (channel.RoomType == CustomRoomType.RoleClaim
            && channel.IsPrivate
            && await RoleClaimCycleValidator.WouldBeSelfReferentialAsync(
                db,
                channel.RoomId,
                channel.AccessRules.Where(r => r.CustomRoleId.HasValue).Select(r => r.CustomRoleId!.Value),
                ct))
        {
            throw new InvalidOperationException(
                "This role-claim room would be self-referential: a required access role can only be claimed inside this same room.");
        }

        channel.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(channel.OwnerAccountClass, ct);

        return MapChannel(await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .ThenInclude(r => r.CustomRole)
            .FirstAsync(c => c.ChannelId == channelId, ct), mask);
    }

    public async Task<bool> ArchiveCustomChannelAsync(Guid channelId, CancellationToken ct = default)
    {
        CustomChannel? channel = await db.CustomChannels.FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel is null)
            return false;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass))
            return false;

        channel.IsArchived = true;
        channel.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(channel.OwnerAccountClass, ct);
        return true;
    }

    public async Task<IReadOnlyList<ClaimableCustomRoleDto>> GetClaimableRolesAsync(
        Guid userId,
        string roomId,
        CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return [];

        List<Role> roles = await db.Roles
            .AsNoTracking()
            .Where(r => r.IsCustom && r.ClaimHostRoomId == roomId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        AccessScope? scope = accessScope.ResolveCurrent();
        if (scope is null)
            return [];

        UserDatabaseLocation actorDb = await userDirectory.ResolveActorDbAsync(ct);
        try
        {
            HashSet<Guid> claimed = (await actorDb.Db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync(ct)).ToHashSet();

            return roles
                .Where(role => InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
                .Select(role => new ClaimableCustomRoleDto
                {
                    RoleId = role.RoleId,
                    Name = role.Name,
                    Description = role.Description,
                    IconName = role.IconName,
                    Claimed = claimed.Contains(role.RoleId),
                })
                .ToList();
        }
        finally
        {
            if (actorDb.DisposeDb)
                await actorDb.Db.DisposeAsync();
        }
    }

    public async Task<bool> ClaimCustomRoleAsync(Guid userId, Guid roleId, string roomId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return false;

        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null || !string.Equals(role.ClaimHostRoomId, roomId, StringComparison.Ordinal))
            return false;

        AccessScope? scope = accessScope.ResolveCurrent();
        if (scope is null
            || !InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
        {
            return false;
        }

        UserDatabaseLocation actorDb = await userDirectory.ResolveActorDbAsync(ct);
        try
        {
            return await AssignCustomRoleOnDbAsync(
                actorDb.Db,
                actorUserId: null,
                targetUserId: userId,
                role,
                ct);
        }
        finally
        {
            if (actorDb.DisposeDb)
                await actorDb.Db.DisposeAsync();
        }
    }

    public async Task<bool> UnclaimCustomRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            return false;

        UserDatabaseLocation actorDb = await userDirectory.ResolveActorDbAsync(ct);
        try
        {
            UserRole? assignment = await actorDb.Db.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
            if (assignment is null)
                return false;

            actorDb.Db.UserRoles.Remove(assignment);
            await actorDb.Db.SaveChangesAsync(ct);
            await EffectiveMaskService.RebuildOnContextAsync(actorDb.Db, userId, ct);
            await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);
            return true;
        }
        finally
        {
            if (actorDb.DisposeDb)
                await actorDb.Db.DisposeAsync();
        }
    }

    public async Task<CustomChannelDto?> GetChannelForUserAsync(Guid userId, string roomId, CancellationToken ct = default)
    {
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, roomId))
            return null;

        CustomChannel? channel = await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .ThenInclude(r => r.CustomRole)
            .FirstOrDefaultAsync(c => c.RoomId == roomId && !c.IsArchived, ct);

        if (channel is null)
            return null;

        AccessScope? scope = accessScope.ResolveCurrent();
        if (scope is null
            || !InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass))
        {
            return null;
        }

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);

        return MapChannel(channel, mask);
    }

    public async Task<ModerationRiskWarningDto?> PreviewAccessRuleRiskAsync(
        Guid customRoleId,
        bool isPublicRoom,
        CancellationToken ct = default)
    {
        if (!isPublicRoom)
            return null;

        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == customRoleId && r.IsCustom, ct);
        if (role is null || !ModerationRiskPermissions.RoleHasHighRiskPermissions(role))
            return null;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return null;

        return new ModerationRiskWarningDto
        {
            RequiresPassword = true,
            RiskyPermissions = ModerationRiskPermissions.GetHighRiskPermissionNames(role),
        };
    }

    public async Task<IReadOnlyList<InfrastructureUserLookupDto>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        IReadOnlyList<(User User, string? TenantDatabaseName)> matches =
            await userDirectory.SearchUsersAsync(query, ct);

        List<InfrastructureUserLookupDto> results = [];
        foreach ((User user, string? tenantDatabaseName) in matches)
            results.Add(await BuildUserLookupAsync(user, tenantDatabaseName, ct));

        return results;
    }

    public async Task<InfrastructureUserLookupDto?> GetUserWithCustomRolesAsync(
        Guid userId,
        string? tenantDatabaseName = null,
        CancellationToken ct = default)
    {
        UserDatabaseLocation? location = await userDirectory.ResolveUserDbAsync(userId, tenantDatabaseName, ct);
        if (location is null)
            return null;

        try
        {
            User? user = await location.Db.Users
                .AsNoTracking()
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId, ct);

            return user is null ? null : await BuildUserLookupAsync(user, location.TenantDatabaseName, ct);
        }
        finally
        {
            if (location.DisposeDb)
                await location.Db.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<AssignableUserDto>> ListAssignableUsersAsync(
        Guid actorUserId,
        Guid roleId,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            return [];

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return [];

        short actorLevel = await GetActorGrantLevelAsync(actorUserId, scope, ct);
        IReadOnlyList<(User User, string? TenantDatabaseName)> users =
            await userDirectory.ListUsersForAssignmentAsync(ct);

        List<AssignableUserDto> results = [];
        foreach ((User user, string? tenantDatabaseName) in users)
        {
            short targetLevel = InfrastructureUserDirectory.GetHighestPlatformRoleBit(user);
            bool alreadyAssigned = user.UserRoles.Any(ur => ur.RoleId == roleId);
            results.Add(new AssignableUserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                TenantDatabaseName = tenantDatabaseName,
                HighestPlatformRoleBit = targetLevel,
                HighestPlatformRoleName = InfrastructureUserDirectory.GetHighestPlatformRoleName(targetLevel),
                AlreadyAssigned = alreadyAssigned,
                CanAssign = !alreadyAssigned
                    && user.UserId != actorUserId
                    && PlatformRoleCatalog.CanGrantRole(actorLevel, targetLevel),
            });
        }

        return results;
    }

    public async Task<int> BulkAssignCustomRoleAsync(
        Guid actorUserId,
        Guid roleId,
        BulkAssignCustomRoleRequest request,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            throw new InvalidOperationException("Custom role was not found.");

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            throw new InvalidOperationException("That role is not available in your account scope.");

        short actorLevel = await GetActorGrantLevelAsync(actorUserId, scope, ct);
        int assigned = 0;

        foreach (BulkAssignUserTarget target in request.Users)
        {
            UserDatabaseLocation? location = await userDirectory.ResolveUserDbAsync(
                target.UserId,
                target.TenantDatabaseName,
                ct);
            if (location is null)
                throw new InvalidOperationException($"User {target.UserId} was not found.");

            try
            {
                User user = await location.Db.Users
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstAsync(u => u.UserId == target.UserId, ct);

                short targetLevel = InfrastructureUserDirectory.GetHighestPlatformRoleBit(user);
                if (!PlatformRoleCatalog.CanGrantRole(actorLevel, targetLevel))
                {
                    throw new InvalidOperationException(
                        $"You cannot assign roles to {user.Username} because their platform role is not below yours.");
                }

                if (await AssignCustomRoleOnDbAsync(location.Db, actorUserId, target.UserId, role, ct))
                    assigned++;
            }
            finally
            {
                if (location.DisposeDb)
                    await location.Db.DisposeAsync();
            }
        }

        return assigned;
    }

    public async Task<bool> AdminAssignCustomRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid roleId,
        string? tenantDatabaseName = null,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            return false;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return false;

        UserDatabaseLocation? location = await userDirectory.ResolveUserDbAsync(targetUserId, tenantDatabaseName, ct);
        if (location is null)
            return false;

        try
        {
            User target = await location.Db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstAsync(u => u.UserId == targetUserId, ct);

            short actorLevel = await GetActorGrantLevelAsync(actorUserId, scope, ct);
            short targetLevel = InfrastructureUserDirectory.GetHighestPlatformRoleBit(target);
            if (!PlatformRoleCatalog.CanGrantRole(actorLevel, targetLevel))
                return false;

            return await AssignCustomRoleOnDbAsync(location.Db, actorUserId, targetUserId, role, ct);
        }
        finally
        {
            if (location.DisposeDb)
                await location.Db.DisposeAsync();
        }
    }

    public async Task<bool> AdminRevokeCustomRoleAsync(
        Guid targetUserId,
        Guid roleId,
        string? tenantDatabaseName = null,
        CancellationToken ct = default)
    {
        Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
        if (role is null)
            return false;

        AccessScope scope = RequireScope();
        if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            return false;

        UserDatabaseLocation? location = await userDirectory.ResolveUserDbAsync(targetUserId, tenantDatabaseName, ct);
        if (location is null)
            return false;

        try
        {
            UserRole? assignment = await location.Db.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == targetUserId && ur.RoleId == roleId, ct);
            if (assignment is null)
                return false;

            location.Db.UserRoles.Remove(assignment);
            await location.Db.SaveChangesAsync(ct);
            await EffectiveMaskService.RebuildOnContextAsync(location.Db, targetUserId, ct);
            await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);
            return true;
        }
        finally
        {
            if (location.DisposeDb)
                await location.Db.DisposeAsync();
        }
    }

    private async Task<InfrastructureUserLookupDto> BuildUserLookupAsync(
        User user,
        string? tenantDatabaseName,
        CancellationToken ct)
    {
        AccessScope scope = RequireScope();
        short highestBit = InfrastructureUserDirectory.GetHighestPlatformRoleBit(user);

        List<Guid> customRoleIds = user.UserRoles
            .Where(ur => ur.Role.IsCustom)
            .Select(ur => ur.RoleId)
            .ToList();

        List<Role> customRoles = customRoleIds.Count == 0
            ? []
            : await db.Roles
                .AsNoTracking()
                .Include(r => r.RolePermissions)
                .Where(r => customRoleIds.Contains(r.RoleId))
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

        return new InfrastructureUserLookupDto
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            TenantDatabaseName = tenantDatabaseName,
            HighestPlatformRoleBit = highestBit,
            HighestPlatformRoleName = InfrastructureUserDirectory.GetHighestPlatformRoleName(highestBit),
            CustomRoles = customRoles
                .Where(role => InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
                .Select(MapRole)
                .ToList(),
        };
    }

    private async Task<short> GetActorHighestPlatformRoleBitAsync(Guid actorUserId, CancellationToken ct)
    {
        UserDatabaseLocation actorDb = await userDirectory.ResolveActorDbAsync(ct);
        try
        {
            User actor = await actorDb.Db.Users
                .AsNoTracking()
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstAsync(u => u.UserId == actorUserId, ct);

            return InfrastructureUserDirectory.GetHighestPlatformRoleBit(actor);
        }
        finally
        {
            if (actorDb.DisposeDb)
                await actorDb.Db.DisposeAsync();
        }
    }

    private async Task<short> GetActorGrantLevelAsync(
        Guid actorUserId,
        AccessScope scope,
        CancellationToken ct)
    {
        if (scope.AccountClass == AccountClass.DevAdmin)
            return PlatformRoles.Owner;

        return await GetActorHighestPlatformRoleBitAsync(actorUserId, ct);
    }

    private static async Task<Guid?> ResolveAssignedByForTargetDbAsync(
        AppDbContext targetDb,
        Guid? actorUserId,
        CancellationToken ct)
    {
        if (actorUserId is null)
            return null;

        bool actorExistsInTargetDb = await targetDb.Users
            .AsNoTracking()
            .AnyAsync(u => u.UserId == actorUserId, ct);

        return actorExistsInTargetDb ? actorUserId : null;
    }

    private async Task<bool> AssignCustomRoleOnDbAsync(
        AppDbContext targetDb,
        Guid? actorUserId,
        Guid targetUserId,
        Role role,
        CancellationToken ct)
    {
        if (!ReferenceEquals(targetDb, db))
            await CustomRoleReplicationService.EnsureRoleSyncedAsync(db, targetDb, role.RoleId, ct);

        bool already = await targetDb.UserRoles.AnyAsync(
            ur => ur.UserId == targetUserId && ur.RoleId == role.RoleId,
            ct);
        if (already)
            return true;

        Guid? assignedBy = await ResolveAssignedByForTargetDbAsync(targetDb, actorUserId, ct);

        targetDb.UserRoles.Add(new UserRole
        {
            UserId = targetUserId,
            RoleId = role.RoleId,
            AssignedBy = assignedBy,
            AssignedAt = DateTime.UtcNow,
        });
        await targetDb.SaveChangesAsync(ct);
        await EffectiveMaskService.RebuildOnContextAsync(targetDb, targetUserId, ct);
        await chatNavNotifier.NotifyNavChangedAsync(role.OwnerAccountClass, ct);
        return true;
    }

    private static string? NormalizeIconName(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        string trimmed = iconName.Trim();
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    private static (string CategoryKey, string CategoryDisplayName) NormalizeCategory(
        string categoryKey,
        string categoryDisplayName)
    {
        string trimmedKey = categoryKey.Trim();
        string trimmedDisplayName = categoryDisplayName.Trim();

        if (ChatRoomCatalog.TryGetCatalogCategoryTemplate(trimmedKey, out ChatRoomCatalog.CatalogCategoryTemplate template))
        {
            return (
                template.Key,
                string.IsNullOrWhiteSpace(trimmedDisplayName) ? template.DisplayName : trimmedDisplayName);
        }

        return (
            string.IsNullOrWhiteSpace(trimmedKey) ? "Custom" : trimmedKey,
            string.IsNullOrWhiteSpace(trimmedDisplayName) ? "Custom" : trimmedDisplayName);
    }

    private AccessScope RequireScope() =>
        accessScope.ResolveCurrent()
        ?? throw new InvalidOperationException("Authenticated account scope is required.");

    private async Task ApplyAccessRulesAsync(
        CustomChannel channel,
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        bool isPrivate,
        string? password,
        Guid actorUserId,
        CancellationToken ct)
    {
        AccessScope scope = RequireScope();

        if (!isPrivate)
        {
            if (rules.Count > 0)
                await EnsurePublicHighRiskConfirmedAsync(rules, password, actorUserId, scope, ct);
            return;
        }

        if (rules.Count == 0)
            throw new InvalidOperationException("Private rooms must specify at least one access role.");

        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            if (rule.CustomRoleId is null && rule.PlatformRoleBit is null)
                throw new InvalidOperationException("Each access rule must specify a custom role or platform role.");

            if (rule.CustomRoleId is not null)
            {
                Role? customRole = await db.Roles.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RoleId == rule.CustomRoleId && r.IsCustom, ct);
                if (customRole is null)
                    throw new InvalidOperationException("Custom access role was not found.");

                if (!InfrastructureAccountScope.CanViewInfrastructure(scope, customRole.OwnerAccountClass))
                {
                    throw new InvalidOperationException(
                        "Custom access roles must belong to the same account scope as the room.");
                }
            }

            channel.AccessRules.Add(new CustomChannelAccessRule
            {
                AccessRuleId = Guid.NewGuid(),
                ChannelId = channel.ChannelId,
                CustomRoleId = rule.CustomRoleId,
                PlatformRoleBit = rule.PlatformRoleBit,
            });
        }
    }

    private async Task EnsurePublicHighRiskConfirmedAsync(
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        string? password,
        Guid actorUserId,
        AccessScope scope,
        CancellationToken ct)
    {
        bool needsPassword = false;
        foreach (CustomChannelAccessRuleInput rule in rules.Where(r => r.CustomRoleId.HasValue))
        {
            Role? role = await db.Roles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoleId == rule.CustomRoleId && r.IsCustom, ct);
            if (role is null)
                throw new InvalidOperationException("Custom access role was not found.");

            if (!InfrastructureAccountScope.CanViewInfrastructure(scope, role.OwnerAccountClass))
            {
                throw new InvalidOperationException(
                    "Custom access roles must belong to the same account scope as the room.");
            }

            if (ModerationRiskPermissions.RoleHasHighRiskPermissions(role))
                needsPassword = true;
        }

        if (!needsPassword)
            return;

        if (string.IsNullOrWhiteSpace(password)
            || !await passwordConfirmation.VerifyAsync(actorUserId, password, ct))
        {
            throw new InvalidOperationException(
                "Adding a high-moderation-risk role to a public room requires password confirmation.");
        }
    }

    private static bool CanEditInfoRoom(UserEffectiveMask mask, CustomChannel channel)
    {
        if (channel.RoomType != CustomRoomType.Info)
            return true;

        if (HasPlatformRole(mask, PlatformRoles.Owner)
            || HasPlatformRole(mask, PlatformRoles.SystemAdministrator))
        {
            return true;
        }

        if (HasPlatformRole(mask, PlatformRoles.Administrator)
            && (DateTime.UtcNow - channel.CreatedAtUtc).TotalDays <= InfoRoomAdminEditDays)
        {
            return true;
        }

        return false;
    }

    private static bool HasPlatformRole(UserEffectiveMask mask, short bit) =>
        BitMask.HasBit(mask.EffectiveRoleMask, bit);

    private static CustomRoleDto MapRole(Role role) =>
        new()
        {
            RoleId = role.RoleId,
            Name = role.Name,
            Description = role.Description,
            IconName = role.IconName,
            ClaimHostRoomId = role.ClaimHostRoomId,
            PermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).OrderBy(id => id).ToList(),
            CreatedAtUtc = role.CreatedAtUtc,
        };

    private static CustomChannelDto MapChannel(CustomChannel channel, UserEffectiveMask mask) =>
        new()
        {
            ChannelId = channel.ChannelId,
            RoomId = channel.RoomId,
            DisplayName = channel.DisplayName,
            IconName = channel.IconName,
            CategoryKey = channel.CategoryKey,
            CategoryDisplayName = channel.CategoryDisplayName,
            RoomType = channel.RoomType.ToString(),
            IsPrivate = channel.IsPrivate,
            InfoContent = channel.InfoContent,
            TieType = channel.TieType.ToString(),
            TieSubjectMask = channel.TieSubjectMask,
            TieSubjectBitIndex = channel.TieSubjectBitIndex,
            TiePlatformRoleBit = channel.TiePlatformRoleBit,
            CreatedAtUtc = channel.CreatedAtUtc,
            UpdatedAtUtc = channel.UpdatedAtUtc,
            CanEditInfo = CanEditInfoRoom(mask, channel),
            AccessRules = channel.AccessRules.Select(rule => new CustomChannelAccessRuleDto
            {
                CustomRoleId = rule.CustomRoleId,
                CustomRoleName = rule.CustomRole?.Name,
                PlatformRoleBit = rule.PlatformRoleBit,
                PlatformRoleName = rule.PlatformRoleBit is short bit && PlatformRoleCatalog.TryGetRoleNameFromBit(bit, out string? platformName)
                    ? platformName
                    : null,
            }).ToList(),
        };
}
