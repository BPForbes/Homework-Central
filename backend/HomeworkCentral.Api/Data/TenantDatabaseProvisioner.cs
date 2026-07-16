using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Data;

/// <summary>Creates tenant databases and applies tenant schema migrations.</summary>
public static class TenantDatabaseProvisioner
{
    public static async Task EnsureDatabaseExistsAsync(
        ITenantConnectionResolver connectionResolver,
        string databaseName,
        CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new(connectionResolver.BuildAdminConnectionString());
        await connection.OpenAsync(ct);

        await using NpgsqlCommand existsCommand = new(
            "SELECT 1 FROM pg_database WHERE datname = @name",
            connection);
        existsCommand.Parameters.AddWithValue("name", databaseName);
        object? exists = await existsCommand.ExecuteScalarAsync(ct);
        if (exists is not null)
            return;

        string escapedName = databaseName.Replace("\"", "\"\"");
        await using NpgsqlCommand createCommand = new($"CREATE DATABASE \"{escapedName}\"", connection);
        try
        {
            await createCommand.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Another startup path created the database between the existence check and CREATE DATABASE.
        }
    }

    public static async Task<bool> DatabaseExistsAsync(
        ITenantConnectionResolver connectionResolver,
        string databaseName,
        CancellationToken ct = default)
    {
        HashSet<string> existing = await FindExistingDatabasesAsync(
            connectionResolver,
            [databaseName],
            ct);
        return existing.Contains(databaseName);
    }

    public static async Task<HashSet<string>> FindExistingDatabasesAsync(
        ITenantConnectionResolver connectionResolver,
        IReadOnlyList<string> databaseNames,
        CancellationToken ct = default)
    {
        HashSet<string> existing = new(StringComparer.Ordinal);
        if (databaseNames.Count == 0)
            return existing;

        await using NpgsqlConnection connection = new(connectionResolver.BuildAdminConnectionString());
        await connection.OpenAsync(ct);

        await using NpgsqlCommand command = new(
            "SELECT datname FROM pg_database WHERE datname = ANY(@names)",
            connection);
        command.Parameters.Add(new NpgsqlParameter("names", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = databaseNames.ToArray(),
        });

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            existing.Add(reader.GetString(0));

        return existing;
    }

    public static async Task EnsureMasterDatabaseExistsAsync(
        ITenantConnectionResolver connectionResolver,
        CancellationToken ct = default)
    {
        await EnsureDatabaseExistsAsync(connectionResolver, connectionResolver.MasterDatabaseName, ct);
    }

    public static async Task<PersonaIdentity> MigrateAndSeedPersonaAsync(
        ITenantConnectionResolver connectionResolver,
        DevAccountDefinition account,
        DevPersonaDefinition persona,
        CancellationToken ct = default)
    {
        string databaseName = DevAccountCatalog.GetPersonaDatabaseName(account, persona);

        await using AppDbContext tenantDb = TenantDbContextFactory.BuildProvisioningContext(
            connectionResolver,
            databaseName);
        await tenantDb.Database.MigrateAsync(ct);

        await AuthorizationSeedData.SeedAsync(tenantDb, ct);
        await HomeworkCentral.Api.Tickets.TicketPortalSeedData.SeedAsync(
            tenantDb,
            HomeworkCentral.Api.Authorization.AccountClass.DeveloperAccount,
            ct: ct);
        await HomeworkCentral.Api.Assessment.ScoringReferenceSeedData.SeedAsync(tenantDb);
        return await TenantBypassSeedData.SeedPersonaAsync(tenantDb, persona, ct);
    }
}
