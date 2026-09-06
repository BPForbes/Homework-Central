using Npgsql;

namespace HomeworkCentral.Api.Tenancy;

public class TenantConnectionResolver : ITenantConnectionResolver
{
    // Per-tenant pool bounds. Every tenant is a distinct Database= value, so Npgsql keys one pool
    // per tenant rather than one pool for the server: the ceiling that matters is
    // (live tenants x MaxPoolSize), not Npgsql's default 100-per-pool. A small cap plus a short
    // idle lifetime keeps the number of *actual* PostgreSQL backend processes — and their
    // memory — proportional to tenants in active use rather than tenants ever touched.
    private const int DefaultMaxPoolSizePerTenant = 4;
    private const int DefaultConnectionIdleLifetimeSeconds = 60;

    private readonly IConfiguration _config;
    private readonly string _baseConnectionString;
    private readonly int _maxPoolSizePerTenant;
    private readonly int _connectionIdleLifetimeSeconds;

    public TenantConnectionResolver(IConfiguration config)
    {
        _config = config;
        _baseConnectionString = ConnectionStringHelpers.ResolveMasterConnection(config);
        MasterDatabaseName = ParseDatabaseName(_baseConnectionString);
        ClusterEnvironment = config["Tenancy:ClusterEnvironment"] ?? "dev";
        _maxPoolSizePerTenant = ReadPositiveInt(config, "Tenancy:MaxPoolSizePerTenant", DefaultMaxPoolSizePerTenant);
        _connectionIdleLifetimeSeconds = ReadPositiveInt(
            config,
            "Tenancy:ConnectionIdleLifetimeSeconds",
            DefaultConnectionIdleLifetimeSeconds);
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
            MaxPoolSize = _maxPoolSizePerTenant,
            MaxAutoPrepare = 16,
            // Capping each pool bounds its width but not how many pools exist. Npgsql's default
            // idle lifetime is 300s, so a tenant touched once holds a server slot for five
            // minutes; at 60s an idle tenant gives its connection back instead.
            ConnectionIdleLifetime = _connectionIdleLifetimeSeconds,
        };
        return builder.ConnectionString;
    }

    public string BuildProvisioningConnectionString(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder = new(_baseConnectionString)
        {
            Database = databaseName,
            // See ITenantConnectionResolver for why this path must not pool. MaxPoolSize alone
            // cannot fix provisioning: it bounds each pool's width, but provisioning creates one
            // pool per tenant and each retains a connection, so 70 personas still exhaust the
            // server's slots however narrow the individual pools are.
            Pooling = false,
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

    private static int ReadPositiveInt(IConfiguration config, string key, int fallback)
    {
        string? raw = config[key];
        return int.TryParse(raw, out int parsed) && parsed > 0 ? parsed : fallback;
    }

    private static string ParseDatabaseName(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        return builder.Database ?? throw new InvalidOperationException("MasterConnection must include a Database value.");
    }
}
