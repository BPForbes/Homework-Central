using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

/// <summary>
/// Tenant-aware, mirroring <see cref="AuthService"/>/<see cref="HomeworkCentral.Api.Captcha.CaptchaRoleService"/>'s
/// DB resolution: a dev persona's <c>Users</c> row lives only in its own tenant database, so
/// writing a <c>UserSubjects</c> row (or even just reading the caller's own claimed subjects)
/// against the injected master <see cref="AppDbContext"/> would either find nothing or, on write,
/// violate the <c>UserId</c>/<c>AssignedBy</c> foreign keys against <c>master.Users</c>.
/// </summary>
public sealed class SubjectClaimService(
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
    IHttpContextAccessor httpContextAccessor,
    IEffectiveMaskService effectiveMaskService) : ISubjectClaimService
{
    public async Task<IReadOnlyList<ClaimableSubjectDto>> GetClaimableSubjectsAsync(Guid userId, CancellationToken ct = default)
    {
        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            List<Subject> generalSubjects = await GeneralSubjectsQuery(db).ToListAsync(ct);

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
        finally
        {
            if (disposeDb)
                await db.DisposeAsync();
        }
    }

    public async Task ClaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default)
    {
        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            Subject subject = await GetGeneralSubjectAsync(db, subjectName, ct);

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

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateUserSubject(ex))
            {
                return;
            }

            await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
        }
        finally
        {
            if (disposeDb)
                await db.DisposeAsync();
        }
    }

    public async Task UnclaimSubjectAsync(Guid userId, string subjectName, CancellationToken ct = default)
    {
        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            Subject subject = await GetGeneralSubjectAsync(db, subjectName, ct);

            UserSubject? assignment = await db.UserSubjects
                .FirstOrDefaultAsync(us => us.UserId == userId && us.SubjectId == subject.SubjectId, ct);
            if (assignment is null)
                return;

            db.UserSubjects.Remove(assignment);
            await db.SaveChangesAsync(ct);
            await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId, ct);
        }
        finally
        {
            if (disposeDb)
                await db.DisposeAsync();
        }
    }

    private async Task<AppDbContext> ResolveDbContextAsync(CancellationToken ct)
    {
        string? tenantDatabaseName = httpContextAccessor.HttpContext?.User
            .FindFirst(TenancyConstants.TenantDbClaimName)?.Value;

        return string.IsNullOrEmpty(tenantDatabaseName)
            ? masterDb
            : await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabaseName, ct);
    }

    private static async Task<Subject> GetGeneralSubjectAsync(AppDbContext db, string subjectName, CancellationToken ct) =>
        await GeneralSubjectsQuery(db).FirstOrDefaultAsync(subject => subject.Name == subjectName, ct)
            ?? throw new InvalidOperationException($"'{subjectName}' is not a claimable general subject.");

    private static IQueryable<Subject> GeneralSubjectsQuery(AppDbContext db) =>
        db.Subjects
            .AsNoTracking()
            .Where(subject => subject.SubjectMask == SubjectMaskNames.General && subject.ParentSubjectId == null);

    private static bool IsDuplicateUserSubject(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
