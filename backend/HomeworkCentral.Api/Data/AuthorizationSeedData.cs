using System.Collections.Concurrent;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public static class AuthorizationSeedData
{
    private static readonly ConcurrentDictionary<string, byte> SeededDatabases = new(StringComparer.Ordinal);

    // Guards the actual seeding work per cache key: without this, two concurrent callers for the
    // same (as-yet-unseeded) database could both see the cache miss and both run the upserts at
    // the same time, racing on the same rows. Lazy<Task> makes the factory passed to GetOrAdd
    // side-effect-free (it just allocates the Lazy) while Lazy<T> itself guarantees the actual
    // seeding delegate only ever runs once per key, even under contention.
    private static readonly ConcurrentDictionary<string, Lazy<Task>> SeedGates = new(StringComparer.Ordinal);

    public static Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        string databaseName = db.Database.GetDbConnection().Database;
        string cacheKey = $"{databaseName}:{AuthorizationCatalog.ContentHashHex}";
        if (SeededDatabases.ContainsKey(cacheKey))
            return Task.CompletedTask;

        Lazy<Task> gate = SeedGates.GetOrAdd(cacheKey, _ => new Lazy<Task>(() => SeedCoreAsync(db, cacheKey, ct)));
        return AwaitAndCleanupGateAsync(cacheKey, gate);
    }

    private static async Task AwaitAndCleanupGateAsync(string cacheKey, Lazy<Task> gate)
    {
        try
        {
            await gate.Value.ConfigureAwait(false);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Lazy<Task>>>)SeedGates)
                .Remove(new KeyValuePair<string, Lazy<Task>>(cacheKey, gate));
        }
    }

    private static async Task SeedCoreAsync(AppDbContext db, string cacheKey, CancellationToken ct)
    {
        // Re-check inside the gate: another caller may have already seeded this exact catalog
        // content (a different cacheKey that happened to finish and populate SeededDatabases)
        // while we were waiting to acquire the gate.
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

        if (await db.Permissions.CountAsync(ct) != AuthorizationCatalog.Permissions.Count)
            return false;

        // Check every role's masks (not just Owner) so a RoleMaskBuilder regression affecting
        // any single role's PermissionMask/RoleMask/FeatureMask reliably forces a reseed instead
        // of silently persisting under a coincidentally-still-matching Owner check.
        List<Role> roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        Dictionary<Guid, Role> rolesById = roles.ToDictionary(role => role.RoleId);

        foreach (AuthorizationCatalog.RoleDefinition roleDefinition in AuthorizationCatalog.Roles)
        {
            if (!rolesById.TryGetValue(roleDefinition.RoleId, out Role? role) || role.Name != roleDefinition.Name)
                return false;

            RoleMaskBuilder.RoleMaskSet expectedMasks = AuthorizationCatalog.GetRoleMasks(roleDefinition.Name);
            if (!MasksMatch(role.PermissionMask, expectedMasks.PermissionMask)
                || !MasksMatch(role.RoleMask, expectedMasks.RoleMask)
                || !MasksMatch(role.FeatureMask, expectedMasks.FeatureMask))
            {
                return false;
            }
        }

        // Check every subject's parent relationship matches the catalog's declared hierarchy.
        List<Subject> subjects = await db.Subjects.AsNoTracking().ToListAsync(ct);
        Dictionary<(string SubjectMask, short BitIndex), Subject> subjectsByKey = subjects
            .ToDictionary(subject => (subject.SubjectMask, subject.BitIndex));
        Dictionary<Guid, Subject> subjectsById = subjects.ToDictionary(subject => subject.SubjectId);

        foreach (AuthorizationCatalog.SubjectDefinition subjectDefinition in AuthorizationCatalog.Subjects)
        {
            if (!subjectsByKey.TryGetValue((subjectDefinition.SubjectMask, subjectDefinition.BitIndex), out Subject? subject)
                || subject.SubjectId != subjectDefinition.SubjectId
                || subject.Name != subjectDefinition.Name)
            {
                return false;
            }

            Guid? expectedParentId = subjectDefinition.ParentName is null
                ? null
                : AuthorizationCatalog.Subjects
                    .FirstOrDefault(s => s.Name == subjectDefinition.ParentName)?.SubjectId;

            if (subject.ParentSubjectId != expectedParentId)
                return false;
        }

        return true;
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
                // RoleId is deterministic from the role name (AuthorizationGuids.Role), so an
                // existing row matched by name should always already have the catalog's ID. A
                // mismatch means either the hashing scheme changed or the row predates it — the
                // RolePermissions/UserRoles FKs below reference this row's *current* RoleId, so
                // silently reassigning it here would orphan those associations. Fail loudly with
                // a clear migration error instead of guessing how to reconcile it.
                if (role.RoleId != roleDefinition.RoleId)
                {
                    throw new InvalidOperationException(
                        $"Role '{roleDefinition.Name}' exists with RoleId {role.RoleId}, but the catalog now " +
                        $"computes {roleDefinition.RoleId} for that name. This database's role IDs are stale " +
                        "relative to AuthorizationGuids.Role and need an explicit data migration before seeding " +
                        "can proceed safely.");
                }

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
            // SubjectId is deterministic from (SubjectMask, BitIndex) (AuthorizationGuids.Subject),
            // so a row matched by that same natural key should already have the catalog's ID.
            // UserSubjects rows reference this subject's *current* SubjectId, so silently
            // reassigning it would orphan those associations — fail fast instead.
            if (subject.SubjectId != subjectDefinition.SubjectId)
            {
                throw new InvalidOperationException(
                    $"Subject '{subjectDefinition.SubjectMask}'/{subjectDefinition.BitIndex} exists with " +
                    $"SubjectId {subject.SubjectId}, but the catalog now computes {subjectDefinition.SubjectId} " +
                    "for that mask/bit. This database's subject IDs are stale relative to " +
                    "AuthorizationGuids.Subject and need an explicit data migration before seeding can " +
                    "proceed safely.");
            }

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
