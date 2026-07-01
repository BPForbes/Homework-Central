using System.Collections.Concurrent;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public static class AuthorizationSeedData
{
    private static readonly ConcurrentDictionary<string, byte> SeededDatabases = new(StringComparer.Ordinal);

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        string databaseName = db.Database.GetDbConnection().Database;
        string cacheKey = $"{databaseName}:{AuthorizationCatalog.ContentHashHex}";
        if (SeededDatabases.ContainsKey(cacheKey))
            return;

        if (await IsCatalogCurrentAsync(db, ct))
        {
            SeededDatabases.TryAdd(cacheKey, 0);
            return;
        }

        await UpsertPermissionsAsync(db, ct);
        await UpsertRolesAsync(db, ct);
        await UpsertSubjectsAsync(db, ct);

        SeededDatabases.TryAdd(cacheKey, 0);
    }

    internal static async Task<bool> IsCatalogCurrentAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Roles.CountAsync(ct) != AuthorizationCatalog.Roles.Count)
            return false;

        if (await db.Subjects.CountAsync(ct) != AuthorizationCatalog.Subjects.Count)
            return false;

        if (await db.RolePermissions.CountAsync(ct) != AuthorizationCatalog.TotalRolePermissionTieCount)
            return false;

        Guid ownerId = AuthorizationGuids.Role("Owner");
        Role? owner = await db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(role => role.RoleId == ownerId && role.Name == "Owner", ct);

        if (owner is null)
            return false;

        RoleMaskBuilder.RoleMaskSet expectedMasks = AuthorizationCatalog.GetRoleMasks("Owner");
        return MasksMatch(owner.PermissionMask, expectedMasks.PermissionMask)
            && MasksMatch(owner.RoleMask, expectedMasks.RoleMask);
    }

    private static async Task UpsertPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        Dictionary<short, Permission> existing = await db.Permissions.ToDictionaryAsync(p => p.PermissionId, ct);

        foreach (AuthorizationCatalog.PermissionDefinition permission in AuthorizationCatalog.Permissions)
        {
            if (existing.TryGetValue(permission.PermissionId, out Permission? row))
            {
                row.Name = permission.Name;
                row.DisplayName = permission.Name;
                row.Description = permission.Description;
                row.Category = "Moderation";
            }
            else
            {
                db.Permissions.Add(new Permission
                {
                    PermissionId = permission.PermissionId,
                    Name = permission.Name,
                    DisplayName = permission.Name,
                    Category = "Moderation",
                    Description = permission.Description,
                });
            }
        }

        HashSet<short> validIds = AuthorizationCatalog.Permissions.Select(p => p.PermissionId).ToHashSet();
        List<Permission> stale = await db.Permissions
            .Where(p => p.Category == "Moderation" && !validIds.Contains(p.PermissionId))
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.RolePermissions.RemoveRange(
                await db.RolePermissions.Where(rp => stale.Select(s => s.PermissionId).Contains(rp.PermissionId)).ToListAsync(ct));
            db.Permissions.RemoveRange(stale);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertRolesAsync(AppDbContext db, CancellationToken ct)
    {
        Dictionary<string, Role> rolesByName = await db.Roles.ToDictionaryAsync(r => r.Name, ct);

        foreach (AuthorizationCatalog.RoleDefinition roleDefinition in AuthorizationCatalog.Roles)
        {
            if (!rolesByName.TryGetValue(roleDefinition.Name, out Role? role))
            {
                role = new Role
                {
                    RoleId = roleDefinition.RoleId,
                    Name = roleDefinition.Name,
                    Description = roleDefinition.Description,
                };
                db.Roles.Add(role);
                rolesByName[roleDefinition.Name] = role;
            }
            else
            {
                role.Description = roleDefinition.Description;
            }

            await db.Entry(role).Collection(r => r.RolePermissions).LoadAsync(ct);

            HashSet<short> desiredPermissionIds = roleDefinition.PermissionIds.ToHashSet();
            List<RolePermission> toRemove = role.RolePermissions
                .Where(rp => !desiredPermissionIds.Contains(rp.PermissionId))
                .ToList();
            foreach (RolePermission rolePermission in toRemove)
                role.RolePermissions.Remove(rolePermission);

            HashSet<short> existingPermissionIds = role.RolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            foreach (short permissionId in desiredPermissionIds.Where(id => !existingPermissionIds.Contains(id)))
            {
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    PermissionId = permissionId,
                });
            }

            RoleMaskBuilder.RoleMaskSet masks = AuthorizationCatalog.GetRoleMasks(roleDefinition.Name);
            role.RoleMask = (System.Collections.BitArray)masks.RoleMask.Clone();
            role.PermissionMask = (System.Collections.BitArray)masks.PermissionMask.Clone();
            role.FeatureMask = (System.Collections.BitArray)masks.FeatureMask.Clone();
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertSubjectsAsync(AppDbContext db, CancellationToken ct)
    {
        Dictionary<(string SubjectMask, short BitIndex), Subject> existing = await db.Subjects
            .ToDictionaryAsync(s => (s.SubjectMask, s.BitIndex), ct);

        Dictionary<string, Subject> subjectsByName = new(StringComparer.Ordinal);

        foreach (AuthorizationCatalog.SubjectDefinition subjectDefinition in AuthorizationCatalog.Subjects)
        {
            UpsertSubject(db, existing, subjectsByName, subjectDefinition);
        }

        await db.SaveChangesAsync(ct);
    }

    private static Subject UpsertSubject(
        AppDbContext db,
        Dictionary<(string SubjectMask, short BitIndex), Subject> existing,
        Dictionary<string, Subject> subjectsByName,
        AuthorizationCatalog.SubjectDefinition subjectDefinition)
    {
        Guid? parentSubjectId = null;
        if (subjectDefinition.ParentName is not null &&
            subjectsByName.TryGetValue(subjectDefinition.ParentName, out Subject? parent))
        {
            parentSubjectId = parent.SubjectId;
        }

        (string SubjectMask, short BitIndex) key = (subjectDefinition.SubjectMask, subjectDefinition.BitIndex);
        if (existing.TryGetValue(key, out Subject? subject))
        {
            subject.Name = subjectDefinition.Name;
            subject.ParentSubjectId = parentSubjectId;
            subjectsByName[subjectDefinition.Name] = subject;
            return subject;
        }

        subject = new Subject
        {
            SubjectId = subjectDefinition.SubjectId,
            Name = subjectDefinition.Name,
            SubjectMask = subjectDefinition.SubjectMask,
            BitIndex = subjectDefinition.BitIndex,
            ParentSubjectId = parentSubjectId,
        };
        db.Subjects.Add(subject);
        existing[key] = subject;
        subjectsByName[subjectDefinition.Name] = subject;
        return subject;
    }

    private static bool MasksMatch(System.Collections.BitArray left, System.Collections.BitArray right)
    {
        int length = Math.Max(left.Length, right.Length);
        for (int bit = 0; bit < length; bit++)
        {
            bool leftValue = bit < left.Length && left[bit];
            bool rightValue = bit < right.Length && right[bit];
            if (leftValue != rightValue)
                return false;
        }

        return true;
    }
}
