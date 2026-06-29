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
        Dictionary<string, Role> rolesByName = await db.Roles.ToDictionaryAsync(r => r.Name, ct);
        Role developerRole = rolesByName["Developer"];
        Role ownerRole = rolesByName["Owner"];

        User devAdmin = await EnsureUserWithRolesAsync(
            db,
            DevBypass.DevAdminEmail,
            DevBypass.DevAdminUsername,
            [ownerRole],
            ct);

        List<User> seededUsers = [devAdmin];

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            User developer = await EnsureUserWithRolesAsync(
                db,
                account.DeveloperEmail,
                account.DeveloperUsername,
                [developerRole],
                ct);
            seededUsers.Add(developer);

            foreach (DevPersonaDefinition persona in account.Personas)
            {
                Role[] personaRoles = persona.Roles
                    .Select(roleName => rolesByName[roleName])
                    .ToArray();

                User user = await EnsureUserWithRolesAsync(
                    db,
                    persona.Email,
                    persona.Username,
                    personaRoles,
                    ct);
                seededUsers.Add(user);
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (User user in seededUsers.DistinctBy(u => u.UserId))
            await effectiveMaskService.RebuildUserEffectiveMaskAsync(user.UserId, ct);
    }

    private static async Task<User> EnsureUserWithRolesAsync(
        AppDbContext db,
        string email,
        string username,
        IReadOnlyList<Role> roles,
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

        HashSet<Guid> desiredRoleIds = roles.Select(role => role.RoleId).ToHashSet();
        List<UserRole> staleAssignments = user.UserRoles
            .Where(userRole => !desiredRoleIds.Contains(userRole.RoleId))
            .ToList();
        foreach (UserRole staleAssignment in staleAssignments)
            user.UserRoles.Remove(staleAssignment);

        HashSet<Guid> existingRoleIds = user.UserRoles
            .Select(userRole => userRole.RoleId)
            .ToHashSet();

        foreach (Role role in roles.Where(role => !existingRoleIds.Contains(role.RoleId)))
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
