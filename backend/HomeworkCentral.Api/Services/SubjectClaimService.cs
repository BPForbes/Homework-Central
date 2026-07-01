using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Self-service claiming of the top-level general subjects (Mathematics, Science, Computer
/// Science, etc. — <see cref="SubjectMaskNames.General"/> subjects with no parent). These are
/// interest/expertise tags any authenticated user can grant or remove for themselves, unlike
/// platform roles (Tutor, Moderator, ...), which are staff-specific and require a granter with
/// ManageRoles — see <see cref="IRoleAssignmentService"/>.
/// </summary>
public interface ISubjectClaimService
{
    Task<IReadOnlyList<ClaimableSubjectDto>> GetClaimableSubjectsAsync(Guid userId, CancellationToken ct = default);
    Task ClaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default);
    Task UnclaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default);
}

public sealed class SubjectClaimService(AppDbContext db, IEffectiveMaskService effectiveMaskService) : ISubjectClaimService
{
    public async Task<IReadOnlyList<ClaimableSubjectDto>> GetClaimableSubjectsAsync(Guid userId, CancellationToken ct = default)
    {
        List<Subject> generalSubjects = await GeneralSubjectsQuery().ToListAsync(ct);

        HashSet<Guid> claimedSubjectIds = await db.UserSubjects
            .AsNoTracking()
            .Where(us => us.UserId == userId)
            .Select(us => us.SubjectId)
            .ToHashSetAsync(ct);

        return generalSubjects
            .OrderBy(subject => subject.Name, StringComparer.Ordinal)
            .Select(subject => new ClaimableSubjectDto
            {
                Name = subject.Name,
                Claimed = claimedSubjectIds.Contains(subject.SubjectId),
            })
            .ToList();
    }

    public async Task ClaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default)
    {
        Subject subject = await GetGeneralSubjectAsync(subjectName, ct);

        bool alreadyClaimed = await db.UserSubjects
            .AnyAsync(us => us.UserId == userId && us.SubjectId == subject.SubjectId, ct);
        if (alreadyClaimed)
            return;

        db.UserSubjects.Add(new UserSubject
        {
            UserId = userId,
            SubjectId = subject.SubjectId,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = userId,
        });

        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
    }

    public async Task UnclaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default)
    {
        Subject subject = await GetGeneralSubjectAsync(subjectName, ct);

        UserSubject? assignment = await db.UserSubjects
            .FirstOrDefaultAsync(us => us.UserId == userId && us.SubjectId == subject.SubjectId, ct);
        if (assignment is null)
            return;

        db.UserSubjects.Remove(assignment);
        await db.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
    }

    private async Task<Subject> GetGeneralSubjectAsync(string subjectName, CancellationToken ct) =>
        await GeneralSubjectsQuery().FirstOrDefaultAsync(subject => subject.Name == subjectName, ct)
            ?? throw new InvalidOperationException($"'{subjectName}' is not a claimable general subject.");

    private IQueryable<Subject> GeneralSubjectsQuery() =>
        db.Subjects
            .AsNoTracking()
            .Where(subject => subject.SubjectMask == SubjectMaskNames.General && subject.ParentSubjectId == null);
}
