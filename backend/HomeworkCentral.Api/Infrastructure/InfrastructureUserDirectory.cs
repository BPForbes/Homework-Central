using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Infrastructure;

public sealed record UserDatabaseLocation(AppDbContext Db, bool DisposeDb, string? TenantDatabaseName);

/// <summary>
/// Resolves which physical database holds a user row and searches users across master/tenant contexts.
/// </summary>
public sealed class InfrastructureUserDirectory(
    AppDbContext masterDb,
    MasterDbContext masterRegistry,
    ITenantDbContextFactory tenantFactory,
    IAccessScopeAccessor accessScope)
{
    public async Task<UserDatabaseLocation> ResolveActorDbAsync(CancellationToken ct = default)
    {
        AccessScope scope = RequireScope();
        if (string.IsNullOrEmpty(scope.TenantDatabaseName))
            return new UserDatabaseLocation(masterDb, DisposeDb: false, TenantDatabaseName: null);

        AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(scope.TenantDatabaseName, ct);
        return new UserDatabaseLocation(tenantDb, DisposeDb: true, scope.TenantDatabaseName);
    }

    public async Task<UserDatabaseLocation?> ResolveUserDbAsync(
        Guid userId,
        string? tenantDatabaseName,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(tenantDatabaseName))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(tenantDatabaseName, ct);
            bool exists = await tenantDb.Users.AsNoTracking().AnyAsync(u => u.UserId == userId, ct);
            return exists
                ? new UserDatabaseLocation(tenantDb, DisposeDb: true, tenantDatabaseName)
                : null;
        }

        if (await masterDb.Users.AsNoTracking().AnyAsync(u => u.UserId == userId, ct))
            return new UserDatabaseLocation(masterDb, DisposeDb: false, TenantDatabaseName: null);

        AccessScope scope = RequireScope();
        if (scope.AccountClass != AccountClass.DevAdmin)
            return null;

        foreach (string databaseName in await ListDevTenantDatabaseNamesAsync(ct))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
            bool exists = await tenantDb.Users.AsNoTracking().AnyAsync(u => u.UserId == userId, ct);
            if (exists)
                return new UserDatabaseLocation(tenantDb, DisposeDb: true, databaseName);

            await tenantDb.DisposeAsync();
        }

        return null;
    }

    public async Task<IReadOnlyList<(User User, string? TenantDatabaseName)>> SearchUsersAsync(
        string query,
        CancellationToken ct = default)
    {
        string term = query.Trim();
        if (term.Length < 2)
            return [];

        AccessScope scope = RequireScope();
        string pattern = $"%{term}%";
        List<(User User, string? TenantDatabaseName)> results = [];

        if (!string.IsNullOrEmpty(scope.TenantDatabaseName))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(scope.TenantDatabaseName, ct);
            await using (tenantDb)
            {
                await SearchDbUsersAsync(tenantDb, pattern, scope.TenantDatabaseName, scope, results, ct);
            }
        }
        else
        {
            await SearchDbUsersAsync(masterDb, pattern, tenantDatabaseName: null, scope, results, ct);

            if (scope.AccountClass == AccountClass.DevAdmin)
            {
                foreach (string databaseName in await ListDevTenantDatabaseNamesAsync(ct))
                {
                    AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
                    await using (tenantDb)
                    {
                        await SearchDbUsersAsync(tenantDb, pattern, databaseName, scope, results, ct);
                    }
                }
            }
        }

        return results
            .OrderBy(entry => entry.User.Username, StringComparer.Ordinal)
            .Take(40)
            .ToList();
    }

    public async Task<IReadOnlyList<(User User, string? TenantDatabaseName)>> ListUsersForAssignmentAsync(
        CancellationToken ct = default)
    {
        AccessScope scope = RequireScope();
        List<(User User, string? TenantDatabaseName)> results = [];

        if (!string.IsNullOrEmpty(scope.TenantDatabaseName))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(scope.TenantDatabaseName, ct);
            await using (tenantDb)
            {
                await ListDbUsersAsync(tenantDb, scope.TenantDatabaseName, scope, results, ct);
            }
        }
        else
        {
            await ListDbUsersAsync(masterDb, tenantDatabaseName: null, scope, results, ct);

            if (scope.AccountClass == AccountClass.DevAdmin)
            {
                foreach (string databaseName in await ListDevTenantDatabaseNamesAsync(ct))
                {
                    AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
                    await using (tenantDb)
                    {
                        await ListDbUsersAsync(tenantDb, databaseName, scope, results, ct);
                    }
                }
            }
        }

        return results
            .OrderBy(entry => entry.User.Username, StringComparer.Ordinal)
            .ToList();
    }

    public static short GetHighestPlatformRoleBit(User user) =>
        PlatformRoleCatalog.GetHighestRoleBit(
            user.UserRoles
                .Select(ur => ur.Role)
                .Where(role => !role.IsCustom)
                .Select(role => role.Name));

    public static string GetHighestPlatformRoleName(short bit) =>
        PlatformRoleCatalog.TryGetRoleNameFromBit(bit, out string? name) ? name : "Guest";

    private static async Task SearchDbUsersAsync(
        AppDbContext db,
        string pattern,
        string? tenantDatabaseName,
        AccessScope scope,
        List<(User User, string? TenantDatabaseName)> results,
        CancellationToken ct)
    {
        List<User> users = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => EF.Functions.ILike(u.Username, pattern) || EF.Functions.ILike(u.Email, pattern))
            .OrderBy(u => u.Username)
            .Take(20)
            .ToListAsync(ct);

        foreach (User user in users)
        {
            if (!CanViewUser(scope, user, tenantDatabaseName))
                continue;

            if (results.Any(entry => entry.User.UserId == user.UserId))
                continue;

            results.Add((user, tenantDatabaseName));
        }
    }

    private static async Task ListDbUsersAsync(
        AppDbContext db,
        string? tenantDatabaseName,
        AccessScope scope,
        List<(User User, string? TenantDatabaseName)> results,
        CancellationToken ct)
    {
        List<User> users = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        foreach (User user in users)
        {
            if (!CanViewUser(scope, user, tenantDatabaseName))
                continue;

            if (results.Any(entry => entry.User.UserId == user.UserId))
                continue;

            results.Add((user, tenantDatabaseName));
        }
    }

    private static bool CanViewUser(AccessScope scope, User user, string? tenantDatabaseName)
    {
        AccountClass userClass = string.IsNullOrEmpty(tenantDatabaseName)
            ? InfrastructureAccountScope.ResolveUserAccountClass(user)
            : AccountClass.DeveloperAccount;

        return InfrastructureAccountScope.CanViewInfrastructure(scope, userClass);
    }

    private async Task<IReadOnlyList<string>> ListDevTenantDatabaseNamesAsync(CancellationToken ct) =>
        await masterRegistry.Tenants
            .AsNoTracking()
            .Select(t => t.DatabaseName)
            .ToListAsync(ct);

    private AccessScope RequireScope() =>
        accessScope.ResolveCurrent()
        ?? throw new InvalidOperationException("Authenticated account scope is required.");
}
