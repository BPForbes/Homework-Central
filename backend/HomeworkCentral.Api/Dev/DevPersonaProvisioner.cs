using System.Collections.Concurrent;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Dev;

public sealed class DevPersonaProvisioner(
    ITenantConnectionResolver connectionResolver,
    ILogger<DevPersonaProvisioner> logger) : IDevPersonaProvisioner
{
    // Each persona creates a database and applies the full EF migration set. Concurrent
    // CREATE DATABASE / migration work contends heavily for Postgres system catalogs on the
    // small local Docker instance, so serial provisioning avoids connection timeouts.
    private const int MaxParallel = 1;

    private readonly ConcurrentDictionary<string, byte> _provisioned = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PersonaIdentity> _identities = new(StringComparer.OrdinalIgnoreCase);

    public int TotalPersonaCount { get; } = DevAccountCatalog.All.Sum(account => account.Personas.Length);

    public int ProvisionedCount => _provisioned.Count;

    public bool IsProvisioned(string databaseName) =>
        _provisioned.ContainsKey(databaseName);

    public bool TryGetPersonaIdentity(string databaseName, out PersonaIdentity identity) =>
        _identities.TryGetValue(databaseName, out identity);

    public void RememberPersonaIdentity(string databaseName, PersonaIdentity identity) =>
        _identities[databaseName] = identity;

    public Task EnsureProvisionedAsync(
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        CancellationToken ct = default)
    {
        string databaseName = DevAccountCatalog.GetPersonaDatabaseName(account, persona);
        if (_provisioned.ContainsKey(databaseName))
            return Task.CompletedTask;

        // ConcurrentDictionary.GetOrAdd's valueFactory can run more than once under contention
        // (only one result gets stored, but a discarded invocation's side effects still ran) —
        // if the factory here called ProvisionPersonaCoreAsync directly, two threads racing on
        // the same database could both actually start migrating/seeding it concurrently.
        // Wrapping in Lazy<Task> makes the factory itself side-effect-free (it just allocates
        // the Lazy), and Lazy<T>'s own synchronization guarantees ProvisionPersonaCoreAsync is
        // invoked exactly once regardless of how many callers race to create it.
        Lazy<Task> lazy = _inFlight.GetOrAdd(
            databaseName,
            _ => new Lazy<Task>(() => ProvisionPersonaCoreAsync(account, persona, databaseName, ct)));

        return AwaitAndCleanupAsync(databaseName, lazy);
    }

    /// <summary>
    /// Logs how many persona databases already exist on disk. Deliberately does NOT mark them
    /// as provisioned: a database existing tells us nothing about whether its schema/seed data
    /// are actually up to date (e.g. after pulling new migrations or catalog changes), so every
    /// persona — new or pre-existing — still goes through the real migrate/seed verification in
    /// <see cref="EnsureProvisionedAsync"/> (on demand at dev login, or via
    /// <see cref="ProvisionAllRemainingAsync"/> when eager provisioning is opted in). That
    /// verification is fast when there's nothing to do (EF's MigrateAsync and this app's own
    /// AuthorizationSeedData caching both no-op quickly against an up-to-date database), so this
    /// doesn't reintroduce a blocking startup cost — it only fixes silently trusting a stale
    /// database.
    /// </summary>
    public async Task InitializeFromExistingDatabasesAsync(CancellationToken ct = default)
    {
        List<string> databaseNames = new(TotalPersonaCount);
        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            foreach (DevPersonaDefinition persona in account.Personas)
                databaseNames.Add(DevAccountCatalog.GetPersonaDatabaseName(account, persona));
        }

        HashSet<string> existingDatabases = await TenantDatabaseProvisioner
            .FindExistingDatabasesAsync(connectionResolver, databaseNames, ct)
            .ConfigureAwait(false);

        if (existingDatabases.Count > 0)
        {
            logger.LogInformation(
                "Detected {ExistingCount}/{TotalCount} existing persona databases; each is re-verified (migrations/seed) before first use.",
                existingDatabases.Count,
                TotalPersonaCount);
        }
    }

    public async Task ProvisionAllRemainingAsync(CancellationToken ct = default)
    {
        List<(DevAccountDefinition Account, DevPersonaDefinition Persona)> remaining = new();

        foreach (DevAccountDefinition account in DevAccountCatalog.All)
        {
            foreach (DevPersonaDefinition persona in account.Personas)
            {
                string databaseName = DevAccountCatalog.GetPersonaDatabaseName(account, persona);
                if (!_provisioned.ContainsKey(databaseName))
                    remaining.Add((account, persona));
            }
        }

        if (remaining.Count == 0)
            return;

        logger.LogInformation(
            "Background provisioning {RemainingCount}/{TotalCount} persona tenant databases (up to {MaxParallel} in parallel)...",
            remaining.Count,
            TotalPersonaCount,
            MaxParallel);

        await Parallel.ForEachAsync(
            remaining,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (item, token) =>
            {
                // Isolate operational DB failures so one persona cannot cancel the shared
                // Parallel.ForEachAsync token (and the remaining migrations). Unexpected
                // exceptions still propagate. Failed personas retry on demand at dev login.
                bool provisioned = await TryProvisionPersonaIsolatedAsync(item.Account, item.Persona, token)
                    .ConfigureAwait(false);
                if (!provisioned)
                    return;

                int current = _provisioned.Count;
                if (current != 1 && current != TotalPersonaCount && current % 10 != 0)
                    return;

                logger.LogInformation(
                    "Provisioned persona database {Current}/{Total}",
                    current,
                    TotalPersonaCount);
            });

        logger.LogInformation("Persona tenant database provisioning complete.");
    }

    internal void MarkProvisioned(string databaseName) =>
        _provisioned.TryAdd(databaseName, 0);

    private Task<bool> TryProvisionPersonaIsolatedAsync(
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        CancellationToken token) =>
        OperationalExceptionGuard.RunAsync(
            async () =>
            {
                await EnsureProvisionedAsync(account, persona, token).ConfigureAwait(false);
                return true;
            },
            ex =>
            {
                // Persona DB names embed an email-derived slug; keep failure logs free of that taint.
                logger.LogWarning(
                    ex,
                    "Skipping failed background persona provisioning for tenant '{TenantSlug}'; it can be retried at login.",
                    account.TenantSlug);
                return false;
            });

    private async Task ProvisionPersonaCoreAsync(
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        string databaseName,
        CancellationToken ct)
    {
        await OperationalExceptionGuard.RunObservingAsync(
            async () =>
            {
                await TenantDatabaseProvisioner.EnsureDatabaseExistsAsync(connectionResolver, databaseName, ct)
                    .ConfigureAwait(false);
                PersonaIdentity identity = await TenantDatabaseProvisioner.MigrateAndSeedPersonaAsync(
                        connectionResolver,
                        account,
                        persona,
                        ct)
                    .ConfigureAwait(false);
                RememberPersonaIdentity(databaseName, identity);
                _provisioned.TryAdd(databaseName, 0);
            },
            ex =>
            {
                logger.LogError(
                    ex,
                    "Failed to provision a persona tenant database for '{TenantSlug}'.",
                    account.TenantSlug);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
    }

    private async Task AwaitAndCleanupAsync(string databaseName, Lazy<Task> lazy)
    {
        try
        {
            await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            // Only remove the entry we actually awaited: a naive TryRemove(databaseName, _)
            // could delete a *different*, newer in-flight operation if another caller already
            // re-entered EnsureProvisionedAsync for this database (e.g. immediately re-queued
            // after a transient failure) between our await completing and this cleanup running.
            ((ICollection<KeyValuePair<string, Lazy<Task>>>)_inFlight)
                .Remove(new KeyValuePair<string, Lazy<Task>>(databaseName, lazy));
        }
    }
}
