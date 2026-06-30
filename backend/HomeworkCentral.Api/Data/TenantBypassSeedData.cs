using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>Seeds personas into an isolated tenant database.</summary>
public static class TenantBypassSeedData
{
    public static async Task SeedPersonasAsync(
        AppDbContext tenantDb,
        DevAccountDefinition account,
        CancellationToken ct = default)
    {
        Dictionary<string, Role> rolesByName = await tenantDb.Roles.ToDictionaryAsync(r => r.Name, ct);

        foreach (DevPersonaDefinition persona in account.Personas)
        {
            Role[] personaRoles = persona.Roles
                .Select(roleName => rolesByName[roleName])
                .ToArray();

            User personaUser = await DevSeedHelpers.EnsureUserWithRolesAsync(
                tenantDb,
                persona.Email,
                persona.Username,
                personaRoles,
                usernameConflictScope: " in tenant database",
                ct);

            await tenantDb.SaveChangesAsync(ct);
            await EffectiveMaskService.RebuildOnContextAsync(tenantDb, personaUser.UserId, ct);
        }
    }
}
