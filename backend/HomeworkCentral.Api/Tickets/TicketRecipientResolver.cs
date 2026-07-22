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
        // Portal mention/staff rules often target AllowedUserId; index once so each
        // rule does not rescan the eligible set. See docs/tickets.md.
        Dictionary<Guid, UserMaskSnapshot> eligibleUsersById = eligibleUsers
            .ToDictionary(user => user.UserId);

        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            if (rule.AllowedUserId is Guid allowedUserId)
            {
                if (eligibleUsersById.TryGetValue(allowedUserId, out UserMaskSnapshot? allowed)
                    && chatRoomAccess.CanAccessRoom(allowed.Masks, allowedUserId, roomId))
                {
                    recipients.Add(allowedUserId);
                }

                continue;
            }

            if (rule.PlatformRoleBit is short platformBit)
            {
                foreach (Guid recipientId in eligibleUsers
                             .Where(snapshot => BitMask.HasBit(snapshot.RoleMask, platformBit))
                             .Where(snapshot => chatRoomAccess.CanAccessRoom(snapshot.Masks, snapshot.UserId, roomId))
                             .Select(snapshot => snapshot.UserId))
                {
                    recipients.Add(recipientId);
                }

                continue;
            }

            if (rule.CustomRoleId is Guid customRoleId)
            {
                foreach (Guid recipientId in eligibleUsers
                             .Where(snapshot => snapshot.Masks.CustomRoleIds.Contains(customRoleId))
                             .Where(snapshot => chatRoomAccess.CanAccessRoom(snapshot.Masks, snapshot.UserId, roomId))
                             .Select(snapshot => snapshot.UserId))
                {
                    recipients.Add(recipientId);
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

    /// <summary>
    /// Loads master masks with one username dictionary for the batch — avoids a Users
    /// query per mask row when filtering DevAdmin. See docs/tickets.md.
    /// </summary>
    private async Task<List<UserMaskSnapshot>> LoadMasksFromMasterAsync(bool realUsersOnly, CancellationToken ct)
    {
        List<UserEffectiveMask> masks = await masterDb.UserEffectiveMasks
            .AsNoTracking()
            .Include(mask => mask.SubjectExpertiseMasks)
            .ToListAsync(ct);

        // One username map for the whole mask set (DevAdmin filter + snapshot build).
        Dictionary<Guid, string> usernames = await masterDb.Users
            .AsNoTracking()
            .Where(user => masks.Select(mask => mask.UserId).Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.Username, ct);

        HashSet<Guid> userIds = masks
            .Where(mask => usernames.ContainsKey(mask.UserId))
            .Select(mask => mask.UserId)
            .ToHashSet();
        Dictionary<Guid, List<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(masterDb, userIds, ct);

        List<UserMaskSnapshot> snapshots = [];
        foreach (UserEffectiveMask mask in masks)
        {
            if (!usernames.TryGetValue(mask.UserId, out string? username))
                continue;

            bool isDevAdmin = string.Equals(username, DevBypass.DevAdminUsername, StringComparison.Ordinal);
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
