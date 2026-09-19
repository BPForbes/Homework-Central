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
        string term = query.Trim().TrimStart('@');
        if (term.Length < 1)
            return [];

        AccessScope scope = RequireScope();
        // Prefix match on username and email (@T → users starting with T).
        string prefix = term.ToLowerInvariant();
        List<(User User, string? TenantDatabaseName)> results = [];
        HashSet<Guid> seenUserIds = [];

        if (!string.IsNullOrEmpty(scope.TenantDatabaseName))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(scope.TenantDatabaseName, ct);
            await using (tenantDb)
            {
                await SearchDbUsersAsync(tenantDb, prefix, scope.TenantDatabaseName, scope, results, seenUserIds, ct);
            }
        }
        else
        {
            await SearchDbUsersAsync(masterDb, prefix, tenantDatabaseName: null, scope, results, seenUserIds, ct);

            if (scope.AccountClass == AccountClass.DevAdmin)
            {
                foreach (string databaseName in await ListDevTenantDatabaseNamesAsync(ct))
                {
                    AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
                    await using (tenantDb)
                    {
                        await SearchDbUsersAsync(tenantDb, prefix, databaseName, scope, results, seenUserIds, ct);
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
        HashSet<Guid> seenUserIds = [];

        if (!string.IsNullOrEmpty(scope.TenantDatabaseName))
        {
            AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(scope.TenantDatabaseName, ct);
            await using (tenantDb)
            {
                await ListDbUsersAsync(tenantDb, scope.TenantDatabaseName, scope, results, seenUserIds, ct);
            }
        }
        else
        {
            await ListDbUsersAsync(masterDb, tenantDatabaseName: null, scope, results, seenUserIds, ct);

            if (scope.AccountClass == AccountClass.DevAdmin)
            {
                foreach (string databaseName in await ListDevTenantDatabaseNamesAsync(ct))
                {
                    AppDbContext tenantDb = await tenantFactory.CreateForRegisteredTenantAsync(databaseName, ct);
                    await using (tenantDb)
                    {
                        await ListDbUsersAsync(tenantDb, databaseName, scope, results, seenUserIds, ct);
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
        string prefix,
        string? tenantDatabaseName,
        AccessScope scope,
        List<(User User, string? TenantDatabaseName)> results,
        HashSet<Guid> seenUserIds,
        CancellationToken ct)
    {
        List<User> users = await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.Username.ToLower().StartsWith(prefix) || u.Email.StartsWith(prefix))
            .OrderBy(u => u.Username)
            .Take(20)
            .ToListAsync(ct);

        foreach (User user in users)
        {
            if (!CanViewUser(scope, user, tenantDatabaseName))
                continue;

            if (!seenUserIds.Add(user.UserId))
                continue;

            results.Add((user, tenantDatabaseName));
        }
    }

    private static async Task ListDbUsersAsync(
        AppDbContext db,
        string? tenantDatabaseName,
        AccessScope scope,
        List<(User User, string? TenantDatabaseName)> results,
        HashSet<Guid> seenUserIds,
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

            if (!seenUserIds.Add(user.UserId))
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
