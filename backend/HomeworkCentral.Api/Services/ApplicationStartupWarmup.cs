using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tickets;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Services;

/// <summary>
/// Master-database migrate and auth seed that must finish before /devlogin is safe.
/// Development defers ticket/neural catalogs until after /healthz is ready; Production
/// finishes those catalogs before Ready. Kept off the Kestrel listen path.
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
                await OperationalExceptionGuard.RunObservingAsync(
                    () => DatabaseStartup.InitializeDevelopmentAsync(services, ct),
                    ex =>
                    {
                        ILogger<Program> logger = services.GetRequiredService<ILogger<Program>>();
                        ITenantConnectionResolver resolver =
                            services.GetRequiredService<ITenantConnectionResolver>();
                        logger.LogCritical(
                            ex,
                            "Database migration failed for master database '{DatabaseName}'. "
                            + "If you upgraded from the single-database layout, reset the local Docker volume: "
                            + "scripts/reset-dev-db.ps1 -Yes (PowerShell) or scripts/reset-dev-db.sh --yes (bash), "
                            + "then run scripts/run-dev.ps1 or scripts/run-dev.sh.",
                            resolver.MasterDatabaseName);
                        return Task.CompletedTask;
                    });
            }
        }

        if (skipDevStartupWarmup)
            return;

        await RunEssentialAuthSeedAsync(services, devBypassEnabled, eagerPersonaProvisioning, ct);
    }

    /// <summary>
    /// Auth, role masks, and /devlogin seed. Ticket portals and neural catalogs stay in
    /// <see cref="RunDeferredCatalogSeedAsync"/> so Development /healthz can become ready
    /// after auth; Production still runs that seed before Ready.
    /// </summary>
    public static async Task RunEssentialAuthSeedAsync(
        IServiceProvider services,
        bool devBypassEnabled,
        bool eagerPersonaProvisioning,
        CancellationToken ct = default)
    {
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

        if (!devBypassEnabled)
            return;

        await TenantRegistrySeedData.SeedAsync(masterRegistry, connectionResolver);
        await DevBypassSeedData.SeedAsync(seedDb, effectiveMaskService);

        startupLogger.LogInformation(
            eagerPersonaProvisioning
                ? "Essential auth seed complete. Ticket catalogs seed separately from auth. Persona databases provision in the background."
                : "Essential auth seed complete. Ticket catalogs seed separately from auth. Persona databases provision on demand at dev login.");
    }

    /// <summary>
    /// Ticket portals, scoring/AI-tracking catalogs, and channel refresh. Login does not
    /// need these rows. Development runs this after Ready; Production runs it before Ready.
    /// </summary>
    public static async Task RunDeferredCatalogSeedAsync(
        IServiceProvider services,
        bool skipDevStartupWarmup,
        bool devBypassEnabled,
        CancellationToken ct = default)
    {
        if (skipDevStartupWarmup)
            return;

        using IServiceScope seedScope = services.CreateScope();
        IServiceProvider sp = seedScope.ServiceProvider;
        AppDbContext seedDb = sp.GetRequiredService<AppDbContext>();
        ILogger<Program> startupLogger = sp.GetRequiredService<ILogger<Program>>();

        // Custom channels / ticket portals live on the master DB and are filtered by
        // OwnerAccountClass (real vs developer). Seed both classes here — persona tenant DBs
        // are not consulted by CustomChannelStore or TicketService.
        await TicketPortalSeedData.SeedAsync(seedDb, AccountClass.RealAccount, startupLogger);
        await TicketPortalSeedData.SeedAsync(seedDb, AccountClass.DeveloperAccount, startupLogger);
        await Assessment.ScoringReferenceSeedData.SeedAsync(seedDb, startupLogger);
        await Assessment.AITrackingCatalogSeedData.SeedAsync(seedDb);

        ICustomChannelStore channelStore = sp.GetRequiredService<ICustomChannelStore>();
        await channelStore.RefreshAsync(ct);
        if (!devBypassEnabled)
            return;

        IDevPersonaProvisioner personaProvisioner = sp.GetRequiredService<IDevPersonaProvisioner>();
        await personaProvisioner.InitializeFromExistingDatabasesAsync();
    }
}
