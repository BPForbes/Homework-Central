using System.Collections.Concurrent;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Tenancy;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Dev;

public sealed class DevPersonaProvisioner(
    ITenantConnectionResolver connectionResolver,
    ILogger<DevPersonaProvisioner> logger) : IDevPersonaProvisioner
{
    private const int MaxParallel = 6;

    private readonly ConcurrentDictionary<string, byte> _provisioned = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);
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

        Task task = _inFlight.GetOrAdd(
            databaseName,
            _ => ProvisionPersonaCoreAsync(account, persona, databaseName, ct));

        return AwaitAndCleanupAsync(databaseName, task);
    }

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

        foreach (string databaseName in existingDatabases)
            _provisioned.TryAdd(databaseName, 0);

        if (existingDatabases.Count > 0)
        {
            logger.LogInformation(
                "Detected {ExistingCount}/{TotalCount} existing persona databases; skipping re-provision.",
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
                await EnsureProvisionedAsync(item.Account, item.Persona, token).ConfigureAwait(false);

                int current = _provisioned.Count;
                if (current == 1 || current == TotalPersonaCount || current % 10 == 0)
                {
                    logger.LogInformation(
                        "Provisioned persona database {Current}/{Total}",
                        current,
                        TotalPersonaCount);
                }
            });

        logger.LogInformation("Persona tenant database provisioning complete.");
    }

    internal void MarkProvisioned(string databaseName) =>
        _provisioned.TryAdd(databaseName, 0);

    private async Task ProvisionPersonaCoreAsync(
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        string databaseName,
        CancellationToken ct)
    {
        try
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
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to provision persona database '{DatabaseName}' for {PersonaEmail}",
                databaseName,
                persona.Email);
            throw;
        }
    }

    private async Task AwaitAndCleanupAsync(string databaseName, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(databaseName, out _);
        }
    }
}
