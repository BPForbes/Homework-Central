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

namespace HomeworkCentral.Api.Chat.Mentions;

public interface IMentionRecipientResolver
{
    Task<HashSet<Guid>> ResolveRecipientsAsync(
        string roomId,
        string groupKey,
        IReadOnlyList<ParsedMention> activeMentions,
        Guid senderId,
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves @username, @role, @everyone, and @here mentions to recipient user IDs.
/// Recipients are limited to the sender's account-class and tenant scope: real accounts
/// may only notify real accounts (master DB); developer personas may only notify users
/// in the same tenant (or any dev persona for DevAdmin senders). @here is already
/// scoped by the real-vs-dev SignalR group bucket.
/// </summary>
public sealed class MentionRecipientResolver(
    AppDbContext masterDb,
    MasterDbContext masterRegistry,
    ITenantDbContextFactory tenantFactory,
    IChatRoomAccessService chatRoomAccess,
    IChatOnlineTracker onlineTracker,
    IRoleAppearanceService roleAppearanceService) : IMentionRecipientResolver
{
    public async Task<HashSet<Guid>> ResolveRecipientsAsync(
        string roomId,
        string groupKey,
        IReadOnlyList<ParsedMention> activeMentions,
        Guid senderId,
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        CancellationToken ct = default)
    {
        HashSet<Guid> recipients = [];
        List<UserMaskSnapshot> eligibleUsers = await LoadEligibleUsersAsync(senderAccountClass, senderTenantDatabaseName, ct);
        // Mentions resolve many @username tokens against one scoped load; the map
        // keeps username lookup off a repeated linear scan.
        Dictionary<string, UserMaskSnapshot> eligibleUsersByUsername = new(StringComparer.OrdinalIgnoreCase);
        foreach (UserMaskSnapshot user in eligibleUsers)
            eligibleUsersByUsername.TryAdd(user.Username, user);

        foreach (ParsedMention mention in activeMentions.Where(m => m.IsActive))
        {
            switch (mention.Kind)
            {
                case MentionKind.User:
                    UserMaskSnapshot? mentionedUser = await ResolveUsernameAsync(
                        mention.Token,
                        senderAccountClass,
                        senderTenantDatabaseName,
                        eligibleUsersByUsername,
                        ct);

                    if (mentionedUser is not null)
                    {
                        if (mentionedUser.UserId == senderId)
                            break;
                        if (!IsEligibleRecipient(senderAccountClass, senderTenantDatabaseName, mentionedUser))
                            break;
                        if (!chatRoomAccess.CanAccessRoom(mentionedUser.Masks, roomId))
                            break;

                        recipients.Add(mentionedUser.UserId);
                        break;
                    }

                    if (await roleAppearanceService.IsMentionablePlatformRoleAsync(mention.Token, ct)
                        && PlatformRoleCatalog.TryGetRoleBit(mention.Token, out short platformBit))
                    {
                        AddPlatformRoleRecipients(
                            recipients,
                            eligibleUsers,
                            roomId,
                            senderId,
                            platformBit);
                        break;
                    }

                    Guid? customRoleId = await roleAppearanceService.TryGetMentionableCustomRoleIdAsync(
                        mention.Token,
                        ct);
                    if (customRoleId is not Guid roleId)
                        break;

                    AddCustomRoleRecipients(
                        recipients,
                        eligibleUsers,
                        roomId,
                        senderId,
                        roleId);
                    break;

                case MentionKind.Role:
                    if (!PlatformRoleCatalog.TryGetRoleBit(mention.Token, out short roleBit))
                        break;

                    if (!await roleAppearanceService.IsMentionablePlatformRoleAsync(mention.Token, ct))
                        break;

                    AddPlatformRoleRecipients(
                        recipients,
                        eligibleUsers,
                        roomId,
                        senderId,
                        roleBit);

                    break;

                case MentionKind.Everyone:
                    recipients.UnionWith(eligibleUsers
                        .Where(snapshot => snapshot.UserId != senderId)
                        .Where(snapshot => chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
                        .Select(snapshot => snapshot.UserId));
                    break;

                case MentionKind.Here:
                    IReadOnlyCollection<Guid> online = onlineTracker.GetOnlineUserIds(groupKey);
                    HashSet<Guid> eligibleIds = eligibleUsers.Select(user => user.UserId).ToHashSet();
                    recipients.UnionWith(online
                        .Where(onlineUserId => onlineUserId != senderId)
                        .Where(eligibleIds.Contains));
                    break;
            }
        }

        return recipients;
    }

    private void AddPlatformRoleRecipients(
        HashSet<Guid> recipients,
        List<UserMaskSnapshot> eligibleUsers,
        string roomId,
        Guid senderId,
        short roleBit)
    {
        recipients.UnionWith(eligibleUsers
            .Where(snapshot => snapshot.UserId != senderId)
            .Where(snapshot => BitMask.HasBit(snapshot.RoleMask, roleBit))
            .Where(snapshot => chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
            .Select(snapshot => snapshot.UserId));
    }

    private void AddCustomRoleRecipients(
        HashSet<Guid> recipients,
        List<UserMaskSnapshot> eligibleUsers,
        string roomId,
        Guid senderId,
        Guid customRoleId)
    {
        recipients.UnionWith(eligibleUsers
            .Where(snapshot => snapshot.UserId != senderId)
            .Where(snapshot => snapshot.Masks.CustomRoleIds.Contains(customRoleId))
            .Where(snapshot => chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
            .Select(snapshot => snapshot.UserId));
    }

    private static bool IsEligibleRecipient(
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        UserMaskSnapshot recipient) =>
        MentionNotifyScope.CanNotify(
            senderAccountClass,
            senderTenantDatabaseName,
            recipient.AccountClass,
            recipient.TenantDatabaseName);

    private async Task<UserMaskSnapshot?> ResolveUsernameAsync(
        string username,
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        Dictionary<string, UserMaskSnapshot> eligibleUsersByUsername,
        CancellationToken ct)
    {
        if (eligibleUsersByUsername.TryGetValue(username, out UserMaskSnapshot? fromEligible))
            return fromEligible;

        if (senderAccountClass == AccountClass.RealAccount)
            return null;

        if (senderAccountClass == AccountClass.DeveloperAccount
            && string.Equals(username, DevBypass.DevAdminUsername, StringComparison.OrdinalIgnoreCase))
        {
            return await LoadDevAdminSnapshotAsync(ct);
        }

        return null;
    }

    private async Task<List<UserMaskSnapshot>> LoadEligibleUsersAsync(
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        CancellationToken ct)
    {
        if (senderAccountClass == AccountClass.RealAccount)
            return await LoadMasksFromMasterAsync(realUsersOnly: true, ct);

        if (senderAccountClass == AccountClass.DeveloperAccount
            && !string.IsNullOrEmpty(senderTenantDatabaseName))
        {
            List<UserMaskSnapshot> snapshots = [];
            await using AppDbContext tenantDb =
                await tenantFactory.CreateForRegisteredTenantAsync(senderTenantDatabaseName, ct);
            snapshots.AddRange(await LoadMasksFromContextAsync(
                tenantDb,
                AccountClass.DeveloperAccount,
                senderTenantDatabaseName,
                ct));
            return snapshots;
        }

        if (senderAccountClass == AccountClass.DevAdmin)
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
            devAdmin.Username,
            AccountClass.DevAdmin,
            null,
            await BuildMaskDtoWithCustomRolesAsync(masterDb, devAdmin.UserId, mask, ct),
            mask.EffectiveRoleMask);
    }

    private static async Task<EffectiveMaskDto> BuildMaskDtoWithCustomRolesAsync(
        AppDbContext db,
        Guid userId,
        UserEffectiveMask mask,
        CancellationToken ct)
    {
        EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
        Dictionary<Guid, HashSet<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(db, [userId], ct);
        if (customRoleIdsByUser.TryGetValue(userId, out HashSet<Guid>? customRoleIds))
            dto.CustomRoleIds = customRoleIds;

        return dto;
    }

    /// <summary>
    /// Loads master masks with one username dictionary for the batch — avoids a Users
    /// query per mask row when filtering DevAdmin. See docs/chat.md.
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
        Dictionary<Guid, HashSet<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(masterDb, userIds, ct);

        return masks
            .Where(mask => usernames.ContainsKey(mask.UserId))
            .Where(mask => !(realUsersOnly
                && string.Equals(usernames[mask.UserId], DevBypass.DevAdminUsername, StringComparison.Ordinal)))
            .Select(mask =>
            {
                string username = usernames[mask.UserId];
                bool isDevAdmin = string.Equals(username, DevBypass.DevAdminUsername, StringComparison.Ordinal);
                return CreateSnapshot(
                    mask,
                    username,
                    isDevAdmin ? AccountClass.DevAdmin : AccountClass.RealAccount,
                    null,
                    customRoleIdsByUser);
            })
            .ToList();
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

        Dictionary<Guid, string> usernames = await db.Users
            .AsNoTracking()
            .Where(user => masks.Select(mask => mask.UserId).Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.Username, ct);

        HashSet<Guid> userIds = masks
            .Where(mask => usernames.ContainsKey(mask.UserId))
            .Select(mask => mask.UserId)
            .ToHashSet();
        Dictionary<Guid, HashSet<Guid>> customRoleIdsByUser =
            await LoadCustomRoleIdsByUserAsync(db, userIds, ct);

        return masks
            .Where(mask => usernames.ContainsKey(mask.UserId))
            .Select(mask => CreateSnapshot(
                mask,
                usernames[mask.UserId],
                accountClass,
                tenantDatabaseName,
                customRoleIdsByUser))
            .ToList();
    }

    private static async Task<Dictionary<Guid, HashSet<Guid>>> LoadCustomRoleIdsByUserAsync(
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

        return assignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(assignment => assignment.RoleId).ToHashSet());
    }

    private static UserMaskSnapshot CreateSnapshot(
        UserEffectiveMask mask,
        string username,
        AccountClass accountClass,
        string? tenantDatabaseName,
        Dictionary<Guid, HashSet<Guid>> customRoleIdsByUser)
    {
        EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
        if (customRoleIdsByUser.TryGetValue(mask.UserId, out HashSet<Guid>? customRoleIds))
            dto.CustomRoleIds = customRoleIds;

        return new UserMaskSnapshot(
            mask.UserId,
            username,
            accountClass,
            tenantDatabaseName,
            dto,
            mask.EffectiveRoleMask);
    }

    private sealed record UserMaskSnapshot(
        Guid UserId,
        string Username,
        AccountClass AccountClass,
        string? TenantDatabaseName,
        EffectiveMaskDto Masks,
        BitArray RoleMask);
}
