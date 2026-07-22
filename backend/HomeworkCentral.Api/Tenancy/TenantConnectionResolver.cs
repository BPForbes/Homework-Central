using Npgsql;

namespace HomeworkCentral.Api.Tenancy;

public class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly IConfiguration _config;
    private readonly string _baseConnectionString;

    public TenantConnectionResolver(IConfiguration config)
    {
        _config = config;
        _baseConnectionString = ConnectionStringHelpers.ResolveMasterConnection(config);
        MasterDatabaseName = ParseDatabaseName(_baseConnectionString);
        ClusterEnvironment = config["Tenancy:ClusterEnvironment"] ?? "dev";
    }

    public string MasterDatabaseName { get; }

    public string ClusterEnvironment { get; }

    public string BuildConnectionString(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(_baseConnectionString)
        {
            Database = databaseName,
            // Each tenant database has a distinct Npgsql pool. Keep those pools tiny or dozens
            // of developer personas can retain more idle connections than PostgreSQL allows.
            MaxPoolSize = 4,
            MaxAutoPrepare = 16,
        };
        return builder.ConnectionString;
    }

    public string BuildAdminConnectionString()
    {
        string? admin = _config.GetConnectionString("PostgresAdmin");
        if (!string.IsNullOrWhiteSpace(admin))
            return admin;

        NpgsqlConnectionStringBuilder builder = new(_baseConnectionString)
        {
            Database = "postgres",
        };
        return builder.ConnectionString;
    }

    private static string ParseDatabaseName(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        return builder.Database ?? throw new InvalidOperationException("MasterConnection must include a Database value.");
    }
}
