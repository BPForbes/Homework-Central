using System.Collections;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public interface IEffectiveMaskService
{
    Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default);
    Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default);
}

public class EffectiveMaskService(AppDbContext db, IRoleMaskService roleMaskService) : IEffectiveMaskService
{
    public async Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.UserEffectiveMasks
            .AsNoTracking()
            .Include(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);
    }

    public async Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default)
    {
        User user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserSubjects).ThenInclude(us => us.Subject)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} was not found.");

        Dictionary<short, Role> rolesByBit = await BuildRolesByBitAsync(ct);

        BitArray roleMask = BitMask.Create(64);
        BitArray moderationMask = BitMask.Create(256);
        BitArray featureMask = BitMask.Create(256);

        foreach (UserRole userRole in user.UserRoles)
        {
            if (!PlatformRoleCatalog.TryGetRoleBit(userRole.Role.Name, out short directBit))
                continue;

            foreach (short bit in RoleHierarchy.ExpandRoleBits(directBit))
            {
                if (!rolesByBit.TryGetValue(bit, out Role? inheritedRole))
                    continue;

                BitMask.SetBit(roleMask, bit);
                moderationMask = BitMask.Or(moderationMask, inheritedRole.PermissionMask);
                featureMask = BitMask.Or(featureMask, inheritedRole.FeatureMask);
            }
        }

        roleMask = roleMaskService.ExpandRoleIdentityMask(roleMask);

        BitArray generalSubjectMask = BitMask.Create(128);
        Dictionary<string, BitArray> expertiseMasks = SubjectExpertiseCatalog.Categories
            .ToDictionary(c => c.ExpertiseMaskName, _ => BitMask.Create(128), StringComparer.Ordinal);

        Dictionary<Guid, Subject> subjectsById = await db.Subjects
            .AsNoTracking()
            .ToDictionaryAsync(s => s.SubjectId, ct);

        foreach (UserSubject userSubject in user.UserSubjects)
            ApplySubjectHierarchy(userSubject.Subject, subjectsById, generalSubjectMask, expertiseMasks);

        BitArray statusMask = BuildDefaultStatusMask();

        UserEffectiveMask? existing = await db.UserEffectiveMasks
            .Include(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        if (existing is null)
        {
            existing = new UserEffectiveMask { UserId = userId };
            db.UserEffectiveMasks.Add(existing);
        }

        existing.EffectiveRoleMask = roleMask;
        existing.EffectiveModerationMask = moderationMask;
        existing.EffectiveFeatureMask = featureMask;
        existing.GeneralSubjectMask = generalSubjectMask;
        existing.StatusMask = statusMask;
        existing.UpdatedAt = DateTime.UtcNow;

        SyncSubjectExpertiseMasks(existing, expertiseMasks);

        await db.SaveChangesAsync(ct);
        return existing;
    }

    private async Task<Dictionary<short, Role>> BuildRolesByBitAsync(CancellationToken ct)
    {
        Dictionary<short, Role> rolesByBit = new();
        List<Role> roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        foreach (Role role in roles)
        {
            if (PlatformRoleCatalog.TryGetRoleBit(role.Name, out short bit))
                rolesByBit[bit] = role;
        }

        return rolesByBit;
    }

    private static void SyncSubjectExpertiseMasks(
        UserEffectiveMask effectiveMask,
        IReadOnlyDictionary<string, BitArray> expertiseMasks)
    {
        Dictionary<string, UserSubjectExpertiseMask> existingByCategory = effectiveMask.SubjectExpertiseMasks
            .ToDictionary(m => m.Category, StringComparer.Ordinal);

        foreach (string category in SubjectExpertiseCatalog.AllExpertiseCategoryNames())
        {
            if (!existingByCategory.TryGetValue(category, out UserSubjectExpertiseMask? row))
            {
                row = new UserSubjectExpertiseMask
                {
                    UserId = effectiveMask.UserId,
                    Category = category,
                };
                effectiveMask.SubjectExpertiseMasks.Add(row);
            }

            row.ExpertiseMask = expertiseMasks[category];
        }
    }

    private static BitArray BuildDefaultStatusMask()
    {
        BitArray mask = BitMask.Create(64);
        BitMask.SetBit(mask, AccountStatus.GoodStanding);
        return mask;
    }

    private static void ApplySubjectHierarchy(
        Subject subject,
        IReadOnlyDictionary<Guid, Subject> subjectsById,
        BitArray generalSubjectMask,
        Dictionary<string, BitArray> expertiseMasks)
    {
        Subject current = subject;
        HashSet<Guid> visited = new();

        while (true)
        {
            if (!visited.Add(current.SubjectId))
                break;

            ApplySubjectBit(current, generalSubjectMask, expertiseMasks);

            if (current.ParentSubjectId is null ||
                !subjectsById.TryGetValue(current.ParentSubjectId.Value, out Subject? parent))
                break;

            current = parent;
        }
    }

    private static void ApplySubjectBit(
        Subject subject,
        BitArray generalSubjectMask,
        Dictionary<string, BitArray> expertiseMasks)
    {
        if (subject.SubjectMask == SubjectMaskNames.General)
        {
            BitMask.SetBit(generalSubjectMask, subject.BitIndex);
            return;
        }

        if (expertiseMasks.TryGetValue(subject.SubjectMask, out BitArray? expertiseMask))
            BitMask.SetBit(expertiseMask, subject.BitIndex);
    }
}
