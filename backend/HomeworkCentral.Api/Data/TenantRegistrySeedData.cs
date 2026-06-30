using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>Registers persona tenant databases in the master registry.</summary>
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
            string normalizedDeveloperEmail = account.DeveloperEmail.ToLowerInvariant();

            foreach (DevPersonaDefinition persona in account.Personas)
            {
                string databaseName = DevAccountCatalog.GetPersonaDatabaseName(account, persona);
                string normalizedPersonaEmail = persona.Email.ToLowerInvariant();
                string personaSlug = DevAccountCatalog.GetPersonaSlug(persona.Email);

                Tenant? existing = await masterDb.Tenants
                    .FirstOrDefaultAsync(t => t.DatabaseName == databaseName, ct);

                if (existing is null)
                {
                    masterDb.Tenants.Add(new Tenant
                    {
                        TenantId = Guid.NewGuid(),
                        DatabaseName = databaseName,
                        ClusterSlug = account.TenantSlug,
                        Slug = personaSlug,
                        DisplayName = persona.Username,
                        DeveloperEmail = normalizedDeveloperEmail,
                        PersonaEmail = normalizedPersonaEmail,
                        ClusterEnvironment = connectionResolver.ClusterEnvironment,
                        CreatedAt = now,
                    });
                }
                else
                {
                    existing.ClusterSlug = account.TenantSlug;
                    existing.Slug = personaSlug;
                    existing.DisplayName = persona.Username;
                    existing.DeveloperEmail = normalizedDeveloperEmail;
                    existing.PersonaEmail = normalizedPersonaEmail;
                    existing.ClusterEnvironment = connectionResolver.ClusterEnvironment;
                }
            }
        }

        await masterDb.SaveChangesAsync(ct);
    }
}
