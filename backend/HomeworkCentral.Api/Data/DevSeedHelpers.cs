using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

internal static class DevSeedHelpers
{
    public static async Task<User> EnsureUserWithRolesAsync(
        AppDbContext db,
        string email,
        string username,
        IReadOnlyList<Role> roles,
        string usernameConflictScope,
        CancellationToken ct = default)
    {
        User? user = await db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        DateTime now = DateTime.UtcNow;
        if (user is null)
        {
            bool usernameTaken = await db.Users
                .AnyAsync(existing => existing.Username == username, ct);
            if (usernameTaken)
            {
                throw new InvalidOperationException(
                    $"Dev seed username '{username}' is already assigned to another account{usernameConflictScope}.");
            }

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
            bool usernameTaken = await db.Users
                .AnyAsync(existing => existing.Username == username && existing.UserId != user.UserId, ct);
            if (usernameTaken)
            {
                throw new InvalidOperationException(
                    $"Dev seed username '{username}' is already assigned to another account{usernameConflictScope}.");
            }

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
