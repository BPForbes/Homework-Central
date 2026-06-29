using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public static class DevBypassSeedData
{
    public static async Task SeedAsync(
        AppDbContext db,
        IEffectiveMaskService effectiveMaskService,
        CancellationToken ct = default)
    {
        Role ownerRole = await db.Roles.FirstAsync(r => r.Name == "Owner", ct);
        Role developerRole = await db.Roles.FirstAsync(r => r.Name == "Developer", ct);

        User devAdmin = await EnsureUserAsync(
            db,
            DevBypass.DevAdminEmail,
            DevBypass.DevAdminUsername,
            ownerRole,
            ct);

        User devDeveloper = await EnsureUserAsync(
            db,
            "devdeveloper@localhost.local",
            "DevDeveloper",
            developerRole,
            ct);

        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(devAdmin.UserId, ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(devDeveloper.UserId, ct);
    }

    private static async Task<User> EnsureUserAsync(
        AppDbContext db,
        string email,
        string username,
        Role role,
        CancellationToken ct)
    {
        User? user = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        DateTime now = DateTime.UtcNow;
        if (user is null)
        {
            user = new User
            {
                UserId = Guid.NewGuid(),
                Email = email,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Users.Add(user);
        }
        else
        {
            user.Username = username;
            user.UpdatedAt = now;
        }

        bool hasRole = user.UserRoles.Any(ur => ur.RoleId == role.RoleId);
        if (!hasRole)
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = role.RoleId,
                AssignedAt = now,
            });
        }

        return user;
    }
}
