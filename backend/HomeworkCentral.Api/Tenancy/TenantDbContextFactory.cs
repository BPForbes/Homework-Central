using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tenancy;

public class TenantDbContextFactory(
    ITenantConnectionResolver connectionResolver,
    MasterDbContext masterDb) : ITenantDbContextFactory
{
    public async Task<AppDbContext> CreateForRegisteredTenantAsync(string databaseName, CancellationToken ct = default)
    {
        if (string.Equals(databaseName, connectionResolver.MasterDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot open the master database as a tenant context.");

        bool registered = await masterDb.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.DatabaseName == databaseName, ct);

        if (!registered)
            throw new InvalidOperationException($"Tenant database '{databaseName}' is not registered.");

        return Build(databaseName);
    }

    public async Task<AppDbContext> CreateForDeveloperEmailAsync(string developerEmail, CancellationToken ct = default)
    {
        string normalizedEmail = developerEmail.ToLowerInvariant();
        Tenant? tenant = await masterDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DeveloperEmail == normalizedEmail, ct);

        if (tenant is null)
            throw new InvalidOperationException($"No tenant is registered for developer email '{developerEmail}'.");

        return Build(tenant.DatabaseName);
    }

    internal static AppDbContext BuildProvisioningContext(ITenantConnectionResolver connectionResolver, string databaseName) =>
        Build(connectionResolver, databaseName);

    private AppDbContext Build(string databaseName) =>
        Build(connectionResolver, databaseName);

    private static AppDbContext Build(ITenantConnectionResolver connectionResolver, string databaseName)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionResolver.BuildConnectionString(databaseName), npgsql =>
                npgsql.MigrationsHistoryTable(TenancyConstants.AppMigrationsHistoryTable))
            .Options;
        return new AppDbContext(options);
    }
}
