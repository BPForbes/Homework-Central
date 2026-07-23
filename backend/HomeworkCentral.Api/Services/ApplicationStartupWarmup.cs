using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Master-database migrate/seed work that must finish before authenticated API traffic is safe.
/// Kept off the Kestrel listen path so /healthz can answer while warmup runs.
/// </summary>
public static class ApplicationStartupWarmup
{
    public static async Task RunAsync(
        IServiceProvider services,
        bool isDevelopment,
        bool skipDevStartupWarmup,
        bool devBypassEnabled,
        bool eagerPersonaProvisioning,
        CancellationToken ct = default)
    {
        if (isDevelopment)
        {
            if (skipDevStartupWarmup)
            {
                ILogger<Program> skipLogger = services.GetRequiredService<ILogger<Program>>();
                skipLogger.LogWarning(
                    "{Flag}=1: skipping development migrations and seed warmup. "
                    + "Only use this with an already initialized local database.",
                    DevStartupWarmup.SkipEnvVarName);
            }
            else
            {
                try
                {
                    await DatabaseStartup.InitializeDevelopmentAsync(services, ct);
                }
                catch (Exception ex)
                {
                    ILogger<Program> logger = services.GetRequiredService<ILogger<Program>>();
                    ITenantConnectionResolver resolver = services.GetRequiredService<ITenantConnectionResolver>();
                    logger.LogCritical(
                        ex,
                        "Database migration failed for master database '{DatabaseName}'. "
                        + "If you upgraded from the single-database layout, reset the local Docker volume: "
                        + "scripts/reset-dev-db.ps1 -Yes (PowerShell) or scripts/reset-dev-db.sh --yes (bash), "
                        + "then run scripts/run-dev.ps1 or scripts/run-dev.sh.",
                        resolver.MasterDatabaseName);
                    throw;
                }
            }
        }

        if (skipDevStartupWarmup)
            return;

        using IServiceScope seedScope = services.CreateScope();
        IServiceProvider sp = seedScope.ServiceProvider;
        ITenantConnectionResolver connectionResolver = sp.GetRequiredService<ITenantConnectionResolver>();
        AppDbContext seedDb = sp.GetRequiredService<AppDbContext>();
        MasterDbContext masterRegistry = sp.GetRequiredService<MasterDbContext>();
        IEffectiveMaskService effectiveMaskService = sp.GetRequiredService<IEffectiveMaskService>();
        ILogger<Program> startupLogger = sp.GetRequiredService<ILogger<Program>>();

        await AuthorizationSeedData.SeedAsync(seedDb);
        IRoleMaskService roleMaskService = sp.GetRequiredService<IRoleMaskService>();
        await roleMaskService.RebuildAllRoleMasksAsync();

        List<Guid> customRoleUserIds = await seedDb.UserRoles
            .Where(ur => ur.Role.IsCustom)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (Guid userId in customRoleUserIds)
            await EffectiveMaskService.RebuildOnContextAsync(seedDb, userId);

        // Custom channels / ticket portals live on the master DB and are filtered by
        // OwnerAccountClass (real vs developer). Seed both classes here — persona tenant DBs
        // are not consulted by CustomChannelStore or TicketService.
        await TicketPortalSeedData.SeedAsync(seedDb, AccountClass.RealAccount, startupLogger);
        await TicketPortalSeedData.SeedAsync(seedDb, AccountClass.DeveloperAccount, startupLogger);
        await Assessment.ScoringReferenceSeedData.SeedAsync(seedDb, startupLogger);

        ICustomChannelStore channelStore = sp.GetRequiredService<ICustomChannelStore>();
        await channelStore.RefreshAsync(ct);
        if (!devBypassEnabled)
            return;

        await TenantRegistrySeedData.SeedAsync(masterRegistry, connectionResolver);
        await DevBypassSeedData.SeedAsync(seedDb, effectiveMaskService);

        IDevPersonaProvisioner personaProvisioner = sp.GetRequiredService<IDevPersonaProvisioner>();
        await personaProvisioner.InitializeFromExistingDatabasesAsync();

        startupLogger.LogInformation(
            eagerPersonaProvisioning
                ? "Essential dev seed complete. Persona databases continue provisioning in the background."
                : "Essential dev seed complete. Persona databases provision on demand at dev login.");
    }
}
