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
        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserSubjects).ThenInclude(us => us.Subject)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} was not found.");

        var roleMask = BitMask.Create(64);
        var moderationMask = BitMask.Create(256);
        var featureMask = BitMask.Create(256);

        foreach (var userRole in user.UserRoles)
        {
            roleMask = BitMask.Or(roleMask, userRole.Role.RoleMask);
            moderationMask = BitMask.Or(moderationMask, userRole.Role.PermissionMask);
            featureMask = BitMask.Or(featureMask, userRole.Role.FeatureMask);
        }

        roleMask = roleMaskService.ExpandRoleIdentityMask(roleMask);

        var generalSubjectMask = BitMask.Create(128);
        var expertiseMasks = SubjectExpertiseCatalog.Categories
            .ToDictionary(c => c.ExpertiseMaskName, _ => BitMask.Create(128), StringComparer.Ordinal);

        var subjectsById = await db.Subjects
            .AsNoTracking()
            .ToDictionaryAsync(s => s.SubjectId, ct);

        foreach (var userSubject in user.UserSubjects)
            ApplySubjectHierarchy(userSubject.Subject, subjectsById, generalSubjectMask, expertiseMasks);

        var statusMask = BuildDefaultStatusMask();

        var existing = await db.UserEffectiveMasks
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

    private static void SyncSubjectExpertiseMasks(
        UserEffectiveMask effectiveMask,
        IReadOnlyDictionary<string, BitArray> expertiseMasks)
    {
        var existingByCategory = effectiveMask.SubjectExpertiseMasks
            .ToDictionary(m => m.Category, StringComparer.Ordinal);

        foreach (var category in SubjectExpertiseCatalog.AllExpertiseCategoryNames())
        {
            if (!existingByCategory.TryGetValue(category, out var row))
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
        var mask = BitMask.Create(64);
        BitMask.SetBit(mask, AccountStatus.GoodStanding);
        return mask;
    }

    private static void ApplySubjectHierarchy(
        Subject subject,
        IReadOnlyDictionary<Guid, Subject> subjectsById,
        BitArray generalSubjectMask,
        Dictionary<string, BitArray> expertiseMasks)
    {
        var current = subject;
        var visited = new HashSet<Guid>();

        while (true)
        {
            if (!visited.Add(current.SubjectId))
                break;

            ApplySubjectBit(current, generalSubjectMask, expertiseMasks);

            if (current.ParentSubjectId is null ||
                !subjectsById.TryGetValue(current.ParentSubjectId.Value, out var parent))
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

        if (expertiseMasks.TryGetValue(subject.SubjectMask, out var expertiseMask))
            BitMask.SetBit(expertiseMask, subject.BitIndex);
    }
}
