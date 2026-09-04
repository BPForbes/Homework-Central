namespace HomeworkCentral.Api.Tenancy;

public interface ITenantConnectionResolver
{
    string MasterDatabaseName { get; }
    string ClusterEnvironment { get; }
    string BuildConnectionString(string databaseName);

    /// <summary>
    /// Connection string for one-shot provisioning work (create/migrate/seed a tenant database),
    /// with pooling disabled. Each tenant has a distinct <c>Database=</c> value, so Npgsql keys a
    /// separate pool per tenant and retains that pool's physical connection after the context is
    /// disposed. Provisioning touches every tenant database exactly once, so pooling there buys
    /// nothing and pins one server slot per tenant provisioned — with 70 dev personas that
    /// exhausts PostgreSQL's global connection slots partway through and the remaining migrations
    /// fail. Use <see cref="BuildConnectionString"/> for request-path work, which does reuse
    /// connections.
    /// </summary>
    string BuildProvisioningConnectionString(string databaseName);

    string BuildAdminConnectionString();
}
