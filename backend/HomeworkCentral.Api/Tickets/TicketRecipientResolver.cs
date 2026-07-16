using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tickets;

public interface ITicketRecipientResolver
{
    Task<HashSet<Guid>> ResolveRecipientsAsync(
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        string roomId,
        AccountClass ownerAccountClass,
        string? tenantDatabaseName,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves inbox recipients from portal mention/staff access rules, limited to users who can
/// access the ticket room within the same account-class scope as the portal channel.
/// </summary>
public sealed class TicketRecipientResolver(
    AppDbContext masterDb,
    MasterDbContext masterRegistry,
    ITenantDbContextFactory tenantFactory,
    IChatRoomAccessService chatRoomAccess) : ITicketRecipientResolver
{
    public async Task<HashSet<Guid>> ResolveRecipientsAsync(
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        string roomId,
        AccountClass ownerAccountClass,
        string? tenantDatabaseName,
        CancellationToken ct = default)
    {
        HashSet<Guid> recipients = [];
        List<UserMaskSnapshot> eligibleUsers = await LoadEligibleUsersAsync(ownerAccountClass, tenantDatabaseName, ct);

        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            if (rule.AllowedUserId is Guid allowedUserId)
            {
                UserMaskSnapshot? allowed = eligibleUsers.FirstOrDefault(user => user.UserId == allowedUserId);
                if (allowed is not null && chatRoomAccess.CanAccessRoom(allowed.Masks, allowedUserId, roomId))
                    recipients.Add(allowedUserId);
                continue;
            }

            if (rule.PlatformRoleBit is short platformBit)
            {
                foreach (UserMaskSnapshot snapshot in eligibleUsers)
                {
                    if (!BitMask.HasBit(snapshot.RoleMask, platformBit))
                        continue;

                    if (chatRoomAccess.CanAccessRoom(snapshot.Masks, snapshot.UserId, roomId))
                        recipients.Add(snapshot.UserId);
                }

                continue;
            }

            if (rule.CustomRoleId is Guid customRoleId)
            {
                foreach (UserMaskSnapshot snapshot in eligibleUsers)
                {
                    if (!snapshot.Masks.CustomRoleIds.Contains(customRoleId))
                        continue;

                    if (chatRoomAccess.CanAccessRoom(snapshot.Masks, snapshot.UserId, roomId))
                        recipients.Add(snapshot.UserId);
                }
            }
        }

        return recipients;
    }

    private async Task<List<UserMaskSnapshot>> LoadEligibleUsersAsync(
        AccountClass ownerAccountClass,
        string? tenantDatabaseName,
        CancellationToken ct)
    {
        if (ownerAccountClass == AccountClass.RealAccount)
            return await LoadMasksFromMasterAsync(realUsersOnly: true, ct);

        if (ownerAccountClass == AccountClass.DeveloperAccount
            && !string.IsNullOrEmpty(tenantDatabaseName))
        {
            await using AppDbContext tenantDb =
                await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabaseName, ct);
            return await LoadMasksFromContextAsync(
                tenantDb,
                AccountClass.DeveloperAccount,
                tenantDatabaseName,
                ct);
        }

        if (ownerAccountClass == AccountClass.DevAdmin)
        {
            List<UserMaskSnapshot> snapshots = [];
            List<string> tenantDatabases = await masterRegistry.Tenants
                .AsNoTracking()
                .Select(tenant => tenant.DatabaseName)
                .ToListAsync(ct);

            foreach (string databaseName in tenantDatabases)
            {
                await using AppDbContext tenantDb =
                    await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
                snapshots.AddRange(await LoadMasksFromContextAsync(
                    tenantDb,
                    AccountClass.DeveloperAccount,
                    databaseName,
                    ct));
            }

            UserMaskSnapshot? devAdmin = await LoadDevAdminSnapshotAsync(ct);
            if (devAdmin is not null)
                snapshots.Add(devAdmin);

            return snapshots;
        }

        return [];
    }

    private async Task<UserMaskSnapshot?> LoadDevAdminSnapshotAsync(CancellationToken ct)
    {
        User? devAdmin = await masterDb.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Username == DevBypass.DevAdminUsername, ct);

        if (devAdmin is null)
            return null;

        UserEffectiveMask? mask = await masterDb.UserEffectiveMasks
            .AsNoTracking()
            .Include(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(m => m.UserId == devAdmin.UserId, ct);

        if (mask is null)
            return null;

        return new UserMaskSnapshot(
            devAdmin.UserId,
            await BuildMaskDtoWithCustomRolesAsync(masterDb, devAdmin.UserId, mask, ct),
            mask.EffectiveRoleMask);
    }

    private async Task<List<UserMaskSnapshot>> LoadMasksFromMasterAsync(bool realUsersOnly, CancellationToken ct)
    {
        List<UserEffectiveMask> masks = await masterDb.UserEffectiveMasks
            .AsNoTracking()
            .Include(mask => mask.SubjectExpertiseMasks)
            .ToListAsync(ct);

        HashSet<Guid> userIds = masks.Select(mask => mask.UserId).ToHashSet();
        Dictionary<Guid, List<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(masterDb, userIds, ct);

        List<UserMaskSnapshot> snapshots = [];
        foreach (UserEffectiveMask mask in masks)
        {
            User? user = await masterDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == mask.UserId, ct);
            if (user is null)
                continue;

            bool isDevAdmin = string.Equals(user.Username, DevBypass.DevAdminUsername, StringComparison.Ordinal);
            if (realUsersOnly && isDevAdmin)
                continue;

            snapshots.Add(CreateSnapshot(mask, customRoleIdsByUser));
        }

        return snapshots;
    }

    private static async Task<List<UserMaskSnapshot>> LoadMasksFromContextAsync(
        AppDbContext db,
        AccountClass accountClass,
        string tenantDatabaseName,
        CancellationToken ct)
    {
        List<UserEffectiveMask> masks = await db.UserEffectiveMasks
            .AsNoTracking()
            .Include(mask => mask.SubjectExpertiseMasks)
            .ToListAsync(ct);

        HashSet<Guid> userIds = masks.Select(mask => mask.UserId).ToHashSet();
        Dictionary<Guid, List<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(db, userIds, ct);

        return masks.Select(mask => CreateSnapshot(mask, customRoleIdsByUser)).ToList();
    }

    private static async Task<Dictionary<Guid, List<Guid>>> LoadCustomRoleIdsByUserAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        List<(Guid UserId, Guid RoleId)> assignments = await db.UserRoles
            .AsNoTracking()
            .Where(userRole => userIds.Contains(userRole.UserId))
            .Join(
                db.Roles.AsNoTracking().Where(role => role.IsCustom),
                userRole => userRole.RoleId,
                role => role.RoleId,
                (userRole, role) => new ValueTuple<Guid, Guid>(userRole.UserId, role.RoleId))
            .ToListAsync(ct);

        Dictionary<Guid, List<Guid>> byUser = [];
        foreach ((Guid userId, Guid roleId) in assignments)
        {
            if (!byUser.TryGetValue(userId, out List<Guid>? roleIds))
            {
                roleIds = [];
                byUser[userId] = roleIds;
            }

            roleIds.Add(roleId);
        }

        return byUser;
    }

    private static async Task<EffectiveMaskDto> BuildMaskDtoWithCustomRolesAsync(
        AppDbContext db,
        Guid userId,
        UserEffectiveMask mask,
        CancellationToken ct)
    {
        EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
        Dictionary<Guid, List<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(db, [userId], ct);
        if (customRoleIdsByUser.TryGetValue(userId, out List<Guid>? customRoleIds))
            dto.CustomRoleIds = customRoleIds;

        return dto;
    }

    private static UserMaskSnapshot CreateSnapshot(
        UserEffectiveMask mask,
        Dictionary<Guid, List<Guid>> customRoleIdsByUser)
    {
        EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
        if (customRoleIdsByUser.TryGetValue(mask.UserId, out List<Guid>? customRoleIds))
            dto.CustomRoleIds = customRoleIds;

        return new UserMaskSnapshot(mask.UserId, dto, mask.EffectiveRoleMask);
    }

    private sealed record UserMaskSnapshot(
        Guid UserId,
        EffectiveMaskDto Masks,
        BitArray RoleMask);
}
