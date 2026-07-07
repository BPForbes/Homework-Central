using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

public static class RoleClaimCycleValidator
{
    /// <summary>
    /// Detects when a private role-claim room requires a custom role that can only be claimed in that same room.
    /// </summary>
    public static async Task<bool> WouldBeSelfReferentialAsync(
        AppDbContext db,
        string roomId,
        IEnumerable<Guid> requiredCustomRoleIds,
        CancellationToken ct = default)
    {
        foreach (Guid roleId in requiredCustomRoleIds.Distinct())
        {
            Role? role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.IsCustom, ct);
            if (role is null)
                continue;

            if (string.Equals(role.ClaimHostRoomId, roomId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static async Task<bool> PlacementWouldBeSelfReferentialAsync(
        AppDbContext db,
        Guid customRoleId,
        string claimHostRoomId,
        CancellationToken ct = default)
    {
        CustomChannel? channel = await db.CustomChannels
            .AsNoTracking()
            .Include(c => c.AccessRules)
            .FirstOrDefaultAsync(c => c.RoomId == claimHostRoomId && !c.IsArchived, ct);

        if (channel is null || channel.RoomType != CustomRoomType.RoleClaim || !channel.IsPrivate)
            return false;

        return channel.AccessRules.Any(rule => rule.CustomRoleId == customRoleId);
    }
}
