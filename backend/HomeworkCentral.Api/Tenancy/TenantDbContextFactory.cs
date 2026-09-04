using HomeworkCentral.Api.Data;
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

    /// <summary>
    /// Context for one-shot provisioning (create/migrate/seed), built on a non-pooled connection
    /// string so the tenant's pool does not retain a server connection once this context is
    /// disposed. See <see cref="ITenantConnectionResolver.BuildProvisioningConnectionString"/>.
    /// </summary>
    internal static AppDbContext BuildProvisioningContext(ITenantConnectionResolver connectionResolver, string databaseName) =>
        BuildFromConnectionString(connectionResolver.BuildProvisioningConnectionString(databaseName));

    private AppDbContext Build(string databaseName) =>
        BuildFromConnectionString(connectionResolver.BuildConnectionString(databaseName));

    private static AppDbContext BuildFromConnectionString(string connectionString)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(TenancyConstants.AppMigrationsHistoryTable))
            .Options;
        return new AppDbContext(options);
    }
}
