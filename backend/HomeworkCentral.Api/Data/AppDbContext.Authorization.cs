using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public partial class AppDbContext
{
    public async Task AssignDefaultRolesAsync(
        User user,
        IEffectiveMaskService effectiveMaskService,
        CancellationToken ct = default)
    {
        var guestRole = await Roles.FirstOrDefaultAsync(r => r.Name == "Guest", ct);
        if (guestRole is not null)
        {
            UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = guestRole.RoleId,
                AssignedAt = DateTime.UtcNow,
            });
        }

        await SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId, ct);
    }

    public async Task PromoteToVerifiedUserAsync(
        User user,
        Guid? assignedBy,
        IEffectiveMaskService effectiveMaskService,
        CancellationToken ct = default)
    {
        var guestRole = await Roles.FirstOrDefaultAsync(r => r.Name == "Guest", ct);
        if (guestRole is not null)
        {
            var guestAssignment = await UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == user.UserId && ur.RoleId == guestRole.RoleId, ct);
            if (guestAssignment is not null)
                UserRoles.Remove(guestAssignment);
        }

        var verifiedRole = await Roles.FirstOrDefaultAsync(r => r.Name == "VerifiedUser", ct)
            ?? throw new InvalidOperationException("VerifiedUser role is not configured.");

        var hasVerified = await UserRoles.AnyAsync(
            ur => ur.UserId == user.UserId && ur.RoleId == verifiedRole.RoleId, ct);

        if (!hasVerified)
        {
            UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = verifiedRole.RoleId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = assignedBy,
            });
        }

        await SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId, ct);
    }
}
