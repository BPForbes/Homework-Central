using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
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

    public static async Task EnsureMasterDatabaseExistsAsync(
        ITenantConnectionResolver connectionResolver,
        CancellationToken ct = default)
    {
        await EnsureDatabaseExistsAsync(connectionResolver, connectionResolver.MasterDatabaseName, ct);
    }

    public static async Task MigrateAndSeedTenantAsync(
        ITenantConnectionResolver connectionResolver,
        DevAccountDefinition account,
        CancellationToken ct = default)
    {
        await using AppDbContext tenantDb = TenantDbContextFactory.BuildProvisioningContext(
            connectionResolver,
            account.TenantDatabaseName);
        await tenantDb.Database.MigrateAsync(ct);

        RoleMaskService tenantRoleMaskService = new(tenantDb);
        await AuthorizationSeedData.SeedAsync(tenantDb, tenantRoleMaskService, ct);
        await TenantBypassSeedData.SeedPersonasAsync(tenantDb, account, ct);
    }
}
