using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tenancy;

public class TenantDbContextFactory(
    ITenantConnectionResolver connectionResolver,
    MasterDbContext masterDb) : ITenantDbContextFactory
{
    public AppDbContext Create(string databaseName)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionResolver.BuildConnectionString(databaseName))
            .Options;
        return new AppDbContext(options);
    }

    public async Task<AppDbContext> CreateForDeveloperEmailAsync(string developerEmail, CancellationToken ct = default)
    {
        string normalizedEmail = developerEmail.ToLowerInvariant();
        Tenant? tenant = await masterDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.DeveloperEmail == normalizedEmail, ct);

        if (tenant is null)
            throw new InvalidOperationException($"No tenant is registered for developer email '{developerEmail}'.");

        return Create(tenant.DatabaseName);
    }
}
