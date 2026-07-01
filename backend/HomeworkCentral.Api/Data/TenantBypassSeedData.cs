using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>Seeds a single persona into an isolated tenant database.</summary>
public static class TenantBypassSeedData
{
    public static async Task<PersonaIdentity> SeedPersonaAsync(
        AppDbContext tenantDb,
        DevPersonaDefinition persona,
        CancellationToken ct = default)
    {
        Dictionary<string, Role> rolesByName = await tenantDb.Roles.ToDictionaryAsync(r => r.Name, ct);

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

        return new PersonaIdentity(personaUser.UserId, personaUser.Username);
    }
}
