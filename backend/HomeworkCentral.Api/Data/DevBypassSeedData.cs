using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>
/// Seeds developer accounts and DevAdmin into the master database when localhost dev bypass is enabled.
/// Personas are seeded separately into isolated tenant databases.
/// </summary>
public static class DevBypassSeedData
{
    /// <summary>Creates or updates DevAdmin and developer accounts on the master database.</summary>
    public static async Task SeedAsync(
        AppDbContext masterDb,
        IEffectiveMaskService effectiveMaskService,
        CancellationToken ct = default)
    {
        DevAccountCatalog.ValidateUniquePersonas();

        Dictionary<string, Role> rolesByName = await masterDb.Roles.ToDictionaryAsync(r => r.Name, ct);
        Role developerRole = rolesByName["Developer"];
        Role ownerRole = rolesByName["Owner"];

        User devAdmin = await DevSeedHelpers.EnsureUserWithRolesAsync(
            masterDb,
            DevBypass.DevAdminEmail,
            DevBypass.DevAdminUsername,
            [ownerRole],
            usernameConflictScope: string.Empty,
            ct);
        await masterDb.SaveChangesAsync(ct);
        await effectiveMaskService.RebuildUserEffectiveMaskAsync(devAdmin.UserId, ct);

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            User developer = await DevSeedHelpers.EnsureUserWithRolesAsync(
                masterDb,
                account.DeveloperEmail,
                account.DeveloperUsername,
                [developerRole],
                usernameConflictScope: string.Empty,
                ct);

            await masterDb.SaveChangesAsync(ct);
            await effectiveMaskService.RebuildUserEffectiveMaskAsync(developer.UserId, ct);
        }
    }
}
