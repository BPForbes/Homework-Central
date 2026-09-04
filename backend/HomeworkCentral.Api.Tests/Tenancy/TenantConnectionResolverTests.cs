using HomeworkCentral.Api.Tenancy;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Tenancy;

/// <summary>
/// Connection-string shaping only — none of these open a connection, so they need no database.
///
/// The behaviour under test is what keeps a fresh dev run from exhausting PostgreSQL's global
/// connection slots: every tenant is a distinct <c>Database=</c> value, so Npgsql keys a separate
/// pool per tenant and retains that pool's physical connection after the context is disposed.
/// Provisioning walks all 70 dev persona databases once, which would pin one server slot per
/// persona if it pooled.
/// </summary>
public class TenantConnectionResolverTests
{
    private const string BaseConnectionString =
        "Host=localhost;Port=5434;Database=homework_central_master;Username=postgres;Password=postgres";

    private static TenantConnectionResolver BuildResolver(params (string Key, string Value)[] settings)
    {
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:MasterConnection"] = BaseConnectionString,
        };
        foreach ((string key, string value) in settings)
            values[key] = value;

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new TenantConnectionResolver(config);
    }

    [Fact]
    public void Provisioning_connection_string_disables_pooling()
    {
        TenantConnectionResolver resolver = BuildResolver();

        NpgsqlConnectionStringBuilder builder =
            new(resolver.BuildProvisioningConnectionString("hc_tenant_science_doc_brown"));

        Assert.False(builder.Pooling);
        Assert.Equal("hc_tenant_science_doc_brown", builder.Database);
    }

    [Fact]
    public void Request_path_connection_string_pools_with_bounded_size_and_idle_lifetime()
    {
        TenantConnectionResolver resolver = BuildResolver();

        NpgsqlConnectionStringBuilder builder =
            new(resolver.BuildConnectionString("hc_tenant_science_doc_brown"));

        Assert.True(builder.Pooling);
        Assert.Equal(4, builder.MaxPoolSize);
        Assert.Equal(60, builder.ConnectionIdleLifetime);
        Assert.Equal(16, builder.MaxAutoPrepare);
    }

    [Fact]
    public void Pool_bounds_are_configurable()
    {
        TenantConnectionResolver resolver = BuildResolver(
            ("Tenancy:MaxPoolSizePerTenant", "7"),
            ("Tenancy:ConnectionIdleLifetimeSeconds", "15"));

        NpgsqlConnectionStringBuilder builder = new(resolver.BuildConnectionString("hc_tenant_math_euclid"));

        Assert.Equal(7, builder.MaxPoolSize);
        Assert.Equal(15, builder.ConnectionIdleLifetime);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void Unusable_pool_bounds_fall_back_to_defaults(string configured)
    {
        TenantConnectionResolver resolver = BuildResolver(
            ("Tenancy:MaxPoolSizePerTenant", configured),
            ("Tenancy:ConnectionIdleLifetimeSeconds", configured));

        NpgsqlConnectionStringBuilder builder = new(resolver.BuildConnectionString("hc_tenant_math_euclid"));

        Assert.Equal(4, builder.MaxPoolSize);
        Assert.Equal(60, builder.ConnectionIdleLifetime);
    }

    [Fact]
    public void Tenant_connection_strings_keep_the_master_host_and_credentials()
    {
        TenantConnectionResolver resolver = BuildResolver();

        foreach (string connectionString in new[]
                 {
                     resolver.BuildConnectionString("hc_tenant_art_frida_kahlo"),
                     resolver.BuildProvisioningConnectionString("hc_tenant_art_frida_kahlo"),
                 })
        {
            NpgsqlConnectionStringBuilder builder = new(connectionString);
            Assert.Equal("localhost", builder.Host);
            Assert.Equal(5434, builder.Port);
            Assert.Equal("postgres", builder.Username);
            Assert.Equal("hc_tenant_art_frida_kahlo", builder.Database);
        }

        Assert.Equal("homework_central_master", resolver.MasterDatabaseName);
    }
}
