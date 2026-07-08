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

        foreach (ParsedMention mention in activeMentions.Where(m => m.IsActive))
        {
            switch (mention.Kind)
            {
                case MentionKind.User:
                    UserMaskSnapshot? mentionedUser = await ResolveUsernameAsync(
                        mention.Token,
                        senderAccountClass,
                        senderTenantDatabaseName,
                        eligibleUsers,
                        ct);

                    if (mentionedUser is not null
                        && mentionedUser.UserId != senderId
                        && IsEligibleRecipient(senderAccountClass, senderTenantDatabaseName, mentionedUser)
                        && chatRoomAccess.CanAccessRoom(mentionedUser.Masks, roomId))
                    {
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
                    if (customRoleId is Guid roleId)
                    {
                        await AddCustomRoleRecipientsAsync(
                            recipients,
                            eligibleUsers,
                            roomId,
                            senderId,
                            roleId,
                            senderAccountClass,
                            senderTenantDatabaseName,
                            ct);
                    }

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
                    foreach (UserMaskSnapshot snapshot in eligibleUsers)
                    {
                        if (snapshot.UserId == senderId)
                            continue;

                        if (chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
                            recipients.Add(snapshot.UserId);
                    }

                    break;

                case MentionKind.Here:
                    IReadOnlyCollection<Guid> online = onlineTracker.GetOnlineUserIds(groupKey);
                    HashSet<Guid> eligibleIds = eligibleUsers.Select(u => u.UserId).ToHashSet();
                    foreach (Guid onlineUserId in online)
                    {
                        if (onlineUserId != senderId && eligibleIds.Contains(onlineUserId))
                            recipients.Add(onlineUserId);
                    }

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
        foreach (UserMaskSnapshot snapshot in eligibleUsers)
        {
            if (snapshot.UserId == senderId)
                continue;

            if (!BitMask.HasBit(snapshot.RoleMask, roleBit))
                continue;

            if (chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
                recipients.Add(snapshot.UserId);
        }
    }

    private async Task AddCustomRoleRecipientsAsync(
        HashSet<Guid> recipients,
        List<UserMaskSnapshot> eligibleUsers,
        string roomId,
        Guid senderId,
        Guid customRoleId,
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        CancellationToken ct)
    {
        HashSet<Guid> holders = await LoadCustomRoleHolderIdsAsync(
            customRoleId,
            senderAccountClass,
            senderTenantDatabaseName,
            ct);

        foreach (UserMaskSnapshot snapshot in eligibleUsers)
        {
            if (snapshot.UserId == senderId)
                continue;

            if (!holders.Contains(snapshot.UserId))
                continue;

            if (chatRoomAccess.CanAccessRoom(snapshot.Masks, roomId))
                recipients.Add(snapshot.UserId);
        }
    }

    private async Task<HashSet<Guid>> LoadCustomRoleHolderIdsAsync(
        Guid customRoleId,
        AccountClass senderAccountClass,
        string? senderTenantDatabaseName,
        CancellationToken ct)
    {
        if (senderAccountClass == AccountClass.RealAccount)
        {
            List<Guid> userIds = await masterDb.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.RoleId == customRoleId)
                .Select(userRole => userRole.UserId)
                .ToListAsync(ct);

            return userIds.ToHashSet();
        }

        if (senderAccountClass == AccountClass.DeveloperAccount
            && !string.IsNullOrEmpty(senderTenantDatabaseName))
        {
            await using AppDbContext tenantDb =
                await tenantFactory.CreateForRegisteredTenantAsync(senderTenantDatabaseName, ct);

            List<Guid> userIds = await tenantDb.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.RoleId == customRoleId)
                .Select(userRole => userRole.UserId)
                .ToListAsync(ct);

            return userIds.ToHashSet();
        }

        if (senderAccountClass == AccountClass.DevAdmin)
        {
            HashSet<Guid> holders = [];
            List<string> tenantDatabases = await masterRegistry.Tenants
                .AsNoTracking()
                .Select(tenant => tenant.DatabaseName)
                .ToListAsync(ct);

            foreach (string databaseName in tenantDatabases)
            {
                await using AppDbContext tenantDb =
                    await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);

                List<Guid> userIds = await tenantDb.UserRoles
                    .AsNoTracking()
                    .Where(userRole => userRole.RoleId == customRoleId)
                    .Select(userRole => userRole.UserId)
                    .ToListAsync(ct);

                foreach (Guid userId in userIds)
                    holders.Add(userId);
            }

            List<Guid> masterUserIds = await masterDb.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.RoleId == customRoleId)
                .Select(userRole => userRole.UserId)
                .ToListAsync(ct);

            foreach (Guid userId in masterUserIds)
                holders.Add(userId);

            return holders;
        }

        return [];
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
        List<UserMaskSnapshot> eligibleUsers,
        CancellationToken ct)
    {
        UserMaskSnapshot? fromEligible = eligibleUsers.FirstOrDefault(
            user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));

        if (fromEligible is not null)
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
            mask.ToEffectiveMaskDto(),
            mask.EffectiveRoleMask);
    }

    private async Task<List<UserMaskSnapshot>> LoadMasksFromMasterAsync(bool realUsersOnly, CancellationToken ct)
    {
        List<UserEffectiveMask> masks = await masterDb.UserEffectiveMasks
            .AsNoTracking()
            .Include(mask => mask.SubjectExpertiseMasks)
            .ToListAsync(ct);

        Dictionary<Guid, string> usernames = await masterDb.Users
            .AsNoTracking()
            .Where(user => masks.Select(mask => mask.UserId).Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.Username, ct);

        List<UserMaskSnapshot> snapshots = [];
        foreach (UserEffectiveMask mask in masks)
        {
            if (!usernames.TryGetValue(mask.UserId, out string? username))
                continue;

            bool isDevAdmin = string.Equals(username, DevBypass.DevAdminUsername, StringComparison.Ordinal);
            if (realUsersOnly && isDevAdmin)
                continue;

            AccountClass accountClass = isDevAdmin ? AccountClass.DevAdmin : AccountClass.RealAccount;
            snapshots.Add(new UserMaskSnapshot(
                mask.UserId,
                username,
                accountClass,
                null,
                mask.ToEffectiveMaskDto(),
                mask.EffectiveRoleMask));
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

        Dictionary<Guid, string> usernames = await db.Users
            .AsNoTracking()
            .Where(user => masks.Select(mask => mask.UserId).Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.Username, ct);

        return masks
            .Where(mask => usernames.ContainsKey(mask.UserId))
            .Select(mask => new UserMaskSnapshot(
                mask.UserId,
                usernames[mask.UserId],
                accountClass,
                tenantDatabaseName,
                mask.ToEffectiveMaskDto(),
                mask.EffectiveRoleMask))
            .ToList();
    }

    private sealed record UserMaskSnapshot(
        Guid UserId,
        string Username,
        AccountClass AccountClass,
        string? TenantDatabaseName,
        EffectiveMaskDto Masks,
        BitArray RoleMask);
}
