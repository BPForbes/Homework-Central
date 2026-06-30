namespace HomeworkCentral.Api.Tenancy;

public static class ConnectionStringHelpers
{
    public static string ResolveMasterConnection(IConfiguration config) =>
        config.GetConnectionString("MasterConnection")
        ?? config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:MasterConnection must be configured.");
}
