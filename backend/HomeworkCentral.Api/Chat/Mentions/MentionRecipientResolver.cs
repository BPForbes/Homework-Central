using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
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
        CancellationToken ct = default);
}

/// <summary>
/// Resolves @username, @role, @everyone, and @here mentions to recipient user IDs.
/// Scans the master database and all registered tenant databases for matching users.
/// @username recipients must pass the same room-access check as @role / @everyone
/// (subject expertise bit, claimed general subject, staff role, or public general room).
/// </summary>
public sealed class MentionRecipientResolver(
    AppDbContext masterDb,
    MasterDbContext masterRegistry,
    ITenantDbContextFactory tenantFactory,
    IChatRoomAccessService chatRoomAccess,
    IChatOnlineTracker onlineTracker) : IMentionRecipientResolver
{
    public async Task<HashSet<Guid>> ResolveRecipientsAsync(
        string roomId,
        string groupKey,
        IReadOnlyList<ParsedMention> activeMentions,
        Guid senderId,
        CancellationToken ct = default)
    {
        HashSet<Guid> recipients = [];
        List<UserMaskSnapshot> allUsers = await LoadAllUserMasksAsync(ct);
        ChatRoomDefinition? room = ChatRoomCatalog.FindById(roomId);

        foreach (ParsedMention mention in activeMentions.Where(m => m.IsActive))
        {
            switch (mention.Kind)
            {
                case MentionKind.User:
                    Guid? userId = await ResolveUsernameAsync(mention.Token, ct);
                    if (userId is null || userId.Value == senderId || room is null)
                        break;

                    UserMaskSnapshot? mentionedUser = allUsers.FirstOrDefault(u => u.UserId == userId.Value);
                    if (mentionedUser is not null && chatRoomAccess.CanAccessRoom(mentionedUser.Masks, room))
                        recipients.Add(userId.Value);

                    break;

                case MentionKind.Role:
                    if (!PlatformRoleCatalog.TryGetRoleBit(mention.Token, out short roleBit))
                        break;

                    foreach (UserMaskSnapshot snapshot in allUsers)
                    {
                        if (snapshot.UserId == senderId)
                            continue;

                        if (!BitMask.HasBit(snapshot.RoleMask, roleBit))
                            continue;

                        if (room is not null && chatRoomAccess.CanAccessRoom(snapshot.Masks, room))
                            recipients.Add(snapshot.UserId);
                    }

                    break;

                case MentionKind.Everyone:
                    foreach (UserMaskSnapshot snapshot in allUsers)
                    {
                        if (snapshot.UserId == senderId)
                            continue;

                        if (room is not null && chatRoomAccess.CanAccessRoom(snapshot.Masks, room))
                            recipients.Add(snapshot.UserId);
                    }

                    break;

                case MentionKind.Here:
                    IReadOnlyCollection<Guid> online = onlineTracker.GetOnlineUserIds(groupKey);
                    foreach (Guid onlineUserId in online)
                    {
                        if (onlineUserId != senderId)
                            recipients.Add(onlineUserId);
                    }

                    break;
            }
        }

        return recipients;
    }

    private async Task<Guid?> ResolveUsernameAsync(string username, CancellationToken ct)
    {
        Guid? masterUser = await masterDb.Users
            .AsNoTracking()
            .Where(user => EF.Functions.ILike(user.Username, username))
            .Select(user => (Guid?)user.UserId)
            .FirstOrDefaultAsync(ct);

        if (masterUser is not null)
            return masterUser;

        List<string> tenantDatabases = await masterRegistry.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.DatabaseName)
            .ToListAsync(ct);

        foreach (string databaseName in tenantDatabases)
        {
            await using AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
            Guid? tenantUser = await tenantDb.Users
                .AsNoTracking()
                .Where(user => EF.Functions.ILike(user.Username, username))
                .Select(user => (Guid?)user.UserId)
                .FirstOrDefaultAsync(ct);

            if (tenantUser is not null)
                return tenantUser;
        }

        return null;
    }

    private async Task<List<UserMaskSnapshot>> LoadAllUserMasksAsync(CancellationToken ct)
    {
        List<UserMaskSnapshot> snapshots = await LoadMasksFromContextAsync(masterDb, ct);

        List<string> tenantDatabases = await masterRegistry.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.DatabaseName)
            .ToListAsync(ct);

        foreach (string databaseName in tenantDatabases)
        {
            await using AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
            snapshots.AddRange(await LoadMasksFromContextAsync(tenantDb, ct));
        }

        return snapshots;
    }

    private static async Task<List<UserMaskSnapshot>> LoadMasksFromContextAsync(AppDbContext db, CancellationToken ct)
    {
        List<UserEffectiveMask> masks = await db.UserEffectiveMasks
            .AsNoTracking()
            .Include(mask => mask.SubjectExpertiseMasks)
            .ToListAsync(ct);

        return masks
            .Select(mask => new UserMaskSnapshot(mask.UserId, mask.ToEffectiveMaskDto(), mask.EffectiveRoleMask))
            .ToList();
    }

    private sealed record UserMaskSnapshot(Guid UserId, EffectiveMaskDto Masks, BitArray RoleMask);
}
