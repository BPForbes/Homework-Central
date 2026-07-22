using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

public interface IEffectiveMaskService
{
    Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default);
    Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default);
    Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default);
}

public class EffectiveMaskService(
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
    IHttpContextAccessor httpContextAccessor,
    IRoleMaskService masterRoleMaskService) : IEffectiveMaskService, IDisposable
{
    private AppDbContext? _tenantDb;
    private IRoleMaskService? _tenantRoleMaskService;
    // This service is scoped, so this cache lives for only one HTTP request or hub invocation.
    // Controllers and downstream services frequently ask for the same mask more than once.
    private Guid? _cachedUserId;
    private string? _cachedTenantDatabase;
    private EffectiveMaskDto? _cachedMaskDto;

    public async Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default)
    {
        (AppDbContext db, _) = await GetContextAsync(ct);
        return await db.UserEffectiveMasks
            .AsNoTracking()
            .Include(m => m.SubjectExpertiseMasks)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);
    }

    public async Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default)
    {
        string? tenantDatabase = ResolveTenantDatabaseName();
        if (_cachedMaskDto is not null
            && _cachedUserId == userId
            && string.Equals(_cachedTenantDatabase, tenantDatabase, StringComparison.Ordinal))
        {
            return _cachedMaskDto;
        }

        (AppDbContext db, _) = await GetContextAsync(ct);
        UserEffectiveMask mask = await GetUserEffectiveMaskAsync(userId, ct)
            ?? await RebuildUserEffectiveMaskAsync(userId, ct);

        HashSet<Guid> customRoleIds = (await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                db.Roles.Where(r => r.IsCustom),
                ur => ur.RoleId,
                role => role.RoleId,
                (ur, role) => role.RoleId)
            .ToListAsync(ct)).ToHashSet();

        EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
        dto.CustomRoleIds = customRoleIds;
        _cachedUserId = userId;
        _cachedTenantDatabase = tenantDatabase;
        _cachedMaskDto = dto;
        return dto;
    }

    public async Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default)
    {
        if (_cachedUserId == userId)
            _cachedMaskDto = null;

        (AppDbContext db, IRoleMaskService roleMaskService) = await GetContextAsync(ct);
        return await RebuildOnContextAsync(db, roleMaskService, userId, ct);
    }

    public static async Task<UserEffectiveMask> RebuildOnContextAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct = default)
    {
        RoleMaskService roleMaskService = new(db);
        return await RebuildOnContextAsync(db, roleMaskService, userId, ct);
    }

    public void Dispose()
    {
        _tenantDb?.Dispose();
    }

    private async Task<(AppDbContext db, IRoleMaskService roleMaskService)> GetContextAsync(CancellationToken ct)
    {
        string? tenantDatabase = ResolveTenantDatabaseName();
        if (string.IsNullOrEmpty(tenantDatabase))
            return (masterDb, masterRoleMaskService);

        _tenantDb ??= await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabase, ct);
        _tenantRoleMaskService ??= new RoleMaskService(_tenantDb);
        return (_tenantDb, _tenantRoleMaskService);
    }

    private string? ResolveTenantDatabaseName()
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return null;

        return httpContext.User.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
    }

    private static async Task<UserEffectiveMask> RebuildOnContextAsync(
        AppDbContext db,
        IRoleMaskService roleMaskService,
        Guid userId,
        CancellationToken ct)
    {
        User user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserSubjects).ThenInclude(us => us.Subject)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} was not found.");

        Dictionary<short, Role> rolesByBit = await BuildRolesByBitAsync(db, ct);

        System.Collections.BitArray roleMask = BitMask.Create(64);
        System.Collections.BitArray moderationMask = BitMask.Create(256);
        System.Collections.BitArray featureMask = BitMask.Create(256);

        foreach (UserRole userRole in user.UserRoles)
        {
            if (!PlatformRoleCatalog.TryGetRoleBit(userRole.Role.Name, out short directBit))
            {
                if (userRole.Role.IsCustom)
                {
                    moderationMask = BitMask.Or(moderationMask, userRole.Role.PermissionMask);
                    featureMask = BitMask.Or(featureMask, userRole.Role.FeatureMask);
                }

                continue;
            }

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

        (System.Collections.BitArray generalSubjectMask, Dictionary<string, System.Collections.BitArray> expertiseMasks) =
            BuildSubjectMasks(user.UserSubjects.Select(us => us.Subject));

        System.Collections.BitArray statusMask = BuildDefaultStatusMask();

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

    private static async Task<Dictionary<short, Role>> BuildRolesByBitAsync(AppDbContext db, CancellationToken ct)
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
        IReadOnlyDictionary<string, System.Collections.BitArray> expertiseMasks)
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

    private static System.Collections.BitArray BuildDefaultStatusMask()
    {
        System.Collections.BitArray mask = BitMask.Create(64);
        BitMask.SetBit(mask, AccountStatus.GoodStanding);
        return mask;
    }

    /// <summary>
    /// Sets exactly one bit per assigned subject: the general bit for a top-level subject, or the
    /// expertise bit for a course. Deliberately does NOT walk up to the parent subject — a course
    /// grant (e.g. Biology) must not set the parent's general bit (Science), because the general
    /// bit unlocks every room in that subject via <c>ChatRoomAccessService.HasSubjectExpertise</c>'s
    /// whole-subject fallback. Whole-subject access comes only from claiming the subject itself.
    /// </summary>
    public static (System.Collections.BitArray GeneralSubjectMask, Dictionary<string, System.Collections.BitArray> ExpertiseMasks)
        BuildSubjectMasks(IEnumerable<Subject> assignedSubjects)
    {
        System.Collections.BitArray generalSubjectMask = BitMask.Create(128);
        Dictionary<string, System.Collections.BitArray> expertiseMasks = SubjectExpertiseCatalog.Categories
            .ToDictionary(c => c.ExpertiseMaskName, _ => BitMask.Create(128), StringComparer.Ordinal);

        foreach (Subject subject in assignedSubjects)
            ApplySubjectBit(subject, generalSubjectMask, expertiseMasks);

        return (generalSubjectMask, expertiseMasks);
    }

    private static void ApplySubjectBit(
        Subject subject,
        System.Collections.BitArray generalSubjectMask,
        Dictionary<string, System.Collections.BitArray> expertiseMasks)
    {
        if (subject.SubjectMask == SubjectMaskNames.General)
        {
            BitMask.SetBit(generalSubjectMask, subject.BitIndex);
            return;
        }

        if (expertiseMasks.TryGetValue(subject.SubjectMask, out System.Collections.BitArray? expertiseMask))
            BitMask.SetBit(expertiseMask, subject.BitIndex);
    }
}
