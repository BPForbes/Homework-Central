namespace HomeworkCentral.Api.Dev;

/// <summary>Creates and migrates persona tenant databases for localhost dev bypass.</summary>
public interface IDevPersonaProvisioner
{
    int TotalPersonaCount { get; }

    int ProvisionedCount { get; }

    bool IsProvisioned(string databaseName);

    bool TryGetPersonaIdentity(string databaseName, out PersonaIdentity identity);

    void RememberPersonaIdentity(string databaseName, PersonaIdentity identity);

    Task EnsureProvisionedAsync(
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        CancellationToken ct = default);

    Task InitializeFromExistingDatabasesAsync(CancellationToken ct = default);

    Task ProvisionAllRemainingAsync(CancellationToken ct = default);
}
