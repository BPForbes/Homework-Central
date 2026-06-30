using Npgsql;

namespace HomeworkCentral.Api.Tenancy;

public class TenantConnectionResolver(IConfiguration config) : ITenantConnectionResolver
{
    private readonly string _baseConnectionString = ResolveMasterConnection(config);

    public string MasterDatabaseName { get; } = ParseDatabaseName(ResolveMasterConnection(config));

    public string ClusterEnvironment { get; } = config["Tenancy:ClusterEnvironment"] ?? "dev";

    public string BuildConnectionString(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(_baseConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }

    public string BuildAdminConnectionString()
    {
        string? admin = config.GetConnectionString("PostgresAdmin");
        if (!string.IsNullOrWhiteSpace(admin))
            return admin;

        NpgsqlConnectionStringBuilder builder = new(_baseConnectionString)
        {
            Database = "postgres",
        };
        return builder.ConnectionString;
    }

    private static string ResolveMasterConnection(IConfiguration config) =>
        config.GetConnectionString("MasterConnection")
        ?? config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:MasterConnection must be configured.");

    private static string ParseDatabaseName(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        return builder.Database ?? throw new InvalidOperationException("MasterConnection must include a Database value.");
    }
}
