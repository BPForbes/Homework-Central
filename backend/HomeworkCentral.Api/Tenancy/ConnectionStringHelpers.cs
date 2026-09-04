using Npgsql;

namespace HomeworkCentral.Api.Tenancy;

public static class ConnectionStringHelpers
{
    public static string ResolveMasterConnection(IConfiguration config)
    {
        string connectionString = config.GetConnectionString("MasterConnection")
            ?? config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:MasterConnection must be configured.");

        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            // PostgreSQL is capped at 50 connections. A smaller application pool avoids idle
            // connectors consuming backend and server memory on an 8 GB development host.
            MaxPoolSize = 20,
            MinPoolSize = 0,
            ConnectionIdleLifetime = 60,
            ConnectionPruningInterval = 10,
            // EF emits a small set of repeated parameterized query shapes. Auto-prepare them
            // after warmup, while bounding the per-connection plan cache.
            MaxAutoPrepare = 32,
            AutoPrepareMinUsages = 3,
            Enlist = false,
        };
        return builder.ConnectionString;
    }
}
