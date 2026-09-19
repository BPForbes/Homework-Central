using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Dev;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace HomeworkCentral.Api.Tests.Dev;

public sealed class StrippedNeuralHostedServiceTests : IClassFixture<StrippedDevHostFixture>
{
    private readonly StrippedDevHostFixture _fixture;

    public StrippedNeuralHostedServiceTests(StrippedDevHostFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void Stripped_development_host_does_not_register_neural_background_workers()
    {
        Skip.IfNot(_fixture.IsDatabaseAvailable, _fixture.SkipReason);

        List<IHostedService> hosted = _fixture.Services.GetServices<IHostedService>().ToList();

        Assert.DoesNotContain(hosted, service => service is NeuralNetTrainingWorker);
        Assert.DoesNotContain(hosted, service => service is NeuralNetCheckpointRefreshService);
        Assert.DoesNotContain(hosted, service => service is ChatMonitoringNeuralModelWarmupService);
    }
}

public sealed class StrippedDevHostFixture : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _adminConnectionString;

    public bool IsDatabaseAvailable { get; }
    public string SkipReason { get; } = "Stripped host tests require Postgres at TEST_DATABASE_URL.";

    public StrippedDevHostFixture()
    {
        _connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
            ?? "Host=127.0.0.1;Port=5432;Database=homework_central_test;Username=postgres;Password=postgres";
        _adminConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN_URL")
            ?? new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" }.ConnectionString;
        IsDatabaseAvailable = CanConnect(_connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:MasterConnection", _connectionString);
        builder.UseSetting("ConnectionStrings:PostgresAdmin", _adminConnectionString);
        builder.UseSetting("Tenancy:ClusterEnvironment", "dev");
        builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-key-32chars!");
        builder.UseSetting("FCaptcha:Secret", "integration-test-fcaptcha-secret-key!");
        builder.UseSetting(DevBypass.EnvVarName, "0");
        builder.UseSetting(DevNeuralEnvironmentPause.EnvVarName, "1");
    }

    private static bool CanConnect(string connectionString)
    {
        try
        {
            using NpgsqlConnection connection = new(connectionString);
            connection.Open();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
