using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Tenant-aware, mirroring <see cref="AuthService"/>'s DB resolution: dev personas (Tutor, Owner,
/// Administrator, etc.) live in their own tenant database, and some of those seeded accounts have
/// no Guest role to begin with, so promotion must work whether or not Guest is present rather than
/// assuming a fixed starting role.
/// </summary>
public sealed class CaptchaRoleService(
    ICaptchaService captcha,
    AppDbContext masterDb,
    ITenantDbContextFactory tenantFactory,
    IHttpContextAccessor httpContextAccessor,
    IEffectiveMaskService effectiveMaskService) : ICaptchaRoleService
{
    public async Task<bool> TryVerifyAndPromoteAsync(
        Guid userId,
        CaptchaSubmissionDto submission,
        CancellationToken ct = default)
    {
        if (!captcha.Validate(submission, CaptchaAction.VerifyRole))
            return false;

        AppDbContext db = await ResolveDbContextAsync(ct);
        bool disposeDb = !ReferenceEquals(db, masterDb);

        try
        {
            User user = await db.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new InvalidOperationException("User was not found.");

            await db.PromoteToVerifiedUserAsync(user, assignedBy: userId, effectiveMaskService, ct);
            return true;
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
}
