using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>Registers developer tenant databases in the master registry.</summary>
public static class TenantRegistrySeedData
{
    public static async Task SeedAsync(
        MasterDbContext masterDb,
        ITenantConnectionResolver connectionResolver,
        CancellationToken ct = default)
    {
        DevAccountCatalog.ValidateUniquePersonas();
        DateTime now = DateTime.UtcNow;

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            string normalizedEmail = account.DeveloperEmail.ToLowerInvariant();
            Tenant? existing = await masterDb.Tenants
                .FirstOrDefaultAsync(t => t.DeveloperEmail == normalizedEmail, ct);

            if (existing is null)
            {
                masterDb.Tenants.Add(new Tenant
                {
                    TenantId = Guid.NewGuid(),
                    DatabaseName = account.TenantDatabaseName,
                    Slug = account.TenantSlug,
                    DisplayName = account.DeveloperUsername,
                    DeveloperEmail = normalizedEmail,
                    ClusterEnvironment = connectionResolver.ClusterEnvironment,
                    CreatedAt = now,
                });
            }
            else
            {
                existing.DatabaseName = account.TenantDatabaseName;
                existing.Slug = account.TenantSlug;
                existing.DisplayName = account.DeveloperUsername;
                existing.ClusterEnvironment = connectionResolver.ClusterEnvironment;
            }
        }

        await masterDb.SaveChangesAsync(ct);
    }
}
