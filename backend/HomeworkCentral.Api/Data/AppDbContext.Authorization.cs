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
        var verifiedRole = await Roles.FirstOrDefaultAsync(r => r.Name == "VerifiedUser", ct);

        if (guestRole is not null)
        {
            UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = guestRole.RoleId,
                AssignedAt = DateTime.UtcNow,
            });
        }

        if (verifiedRole is not null)
        {
            UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = verifiedRole.RoleId,
                AssignedAt = DateTime.UtcNow,
            });
        }

        await SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId, ct);
    }
}
