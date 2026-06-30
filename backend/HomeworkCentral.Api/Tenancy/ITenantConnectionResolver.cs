namespace HomeworkCentral.Api.Tenancy;

public interface ITenantConnectionResolver
{
    string MasterDatabaseName { get; }
    string ClusterEnvironment { get; }
    string BuildConnectionString(string databaseName);
    string BuildAdminConnectionString();
}
