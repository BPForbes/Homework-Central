using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Assessment;

public class AITrackingServiceTests
{
    private const string IsolatedDatabaseName = "homework_central_test_ai_tracking";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    [SkippableFact]
    public async Task Custom_lineage_sessions_can_be_recorded_queried_and_deleted()
    {
        string connectionString = ResolveConnectionString();
        Skip.IfNot(await CanConnectAsync(connectionString), "Requires a reachable Postgres server.");

        await using AppDbContext db = await CreateMigratedDatabaseAsync(connectionString);
        await AITrackingCatalogSeedData.SeedAsync(db);
        AITrackingService tracking = new(db);

        AIModelLineageDto lineage = await tracking.RegisterCustomLineageAsync(
            new RegisterAIModelLineageRequest
            {
                Slug = "hr-intake",
                DisplayName = "HR intake",
                Categories =
                [
                    new RegisterAICategoryRequest { Slug = "policy-question", DisplayName = "Policy question" },
                    new RegisterAICategoryRequest { Slug = "escalation", DisplayName = "Escalation", IsCatchAll = true },
                ],
            },
            CancellationToken.None);

        Assert.False(lineage.IsBuiltIn);
        Assert.Equal(2, lineage.CategoryCount);

        long sessionId = await tracking.RecordCategoryWeightsAsync(
            "hr-intake",
            ticketId: null,
            messageIndex: 0,
            modelVersion: "custom-v1",
            new Dictionary<string, double> { ["policy-question"] = 0.8, ["unknown"] = 0.2, ["escalation"] = 0 },
            createdByUserId: null,
            CancellationToken.None);

        AITrackingSessionDto? session = await tracking.GetSessionAsync(sessionId, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("hr-intake", session.LineageSlug);
        Assert.Equal(["policy-question"], session.CategoryWeights.Select(weight => weight.CategorySlug).ToArray());

        PagedResultDto<AITrackingSessionDto> queried = await tracking.QuerySessionsAsync(
            "hr-intake", ticketId: null, createdByUserId: null, beforeUtc: null, limit: 20, CancellationToken.None);
        Assert.Single(queried.Items);

        int deleted = await tracking.DeleteSessionsForLineageAsync("hr-intake", CancellationToken.None);
        Assert.Equal(1, deleted);
        Assert.Null(await tracking.GetSessionAsync(sessionId, CancellationToken.None));

        Assert.True(await tracking.DeleteCustomLineageAsync("hr-intake", CancellationToken.None));
        Assert.DoesNotContain(
            await tracking.ListLineagesAsync(CancellationToken.None),
            row => row.Slug == "hr-intake");
    }

    [SkippableFact]
    public async Task Built_in_lineage_cannot_be_deleted()
    {
        string connectionString = ResolveConnectionString();
        Skip.IfNot(await CanConnectAsync(connectionString), "Requires a reachable Postgres server.");

        await using AppDbContext db = await CreateMigratedDatabaseAsync(connectionString);
        await AITrackingCatalogSeedData.SeedAsync(db);
        AITrackingService tracking = new(db);

        Assert.False(await tracking.DeleteCustomLineageAsync(AITrackingCatalog.ModerationSlug, CancellationToken.None));
        Assert.Contains(
            await tracking.ListLineagesAsync(CancellationToken.None),
            row => row.Slug == AITrackingCatalog.ModerationSlug && row.IsBuiltIn);
    }

    private static string ResolveConnectionString()
    {
        string baseConnectionString =
            Environment.GetEnvironmentVariable("TEST_AITRACKING_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
            ?? DefaultConnectionString;

        return new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = IsolatedDatabaseName,
        }.ConnectionString;
    }

    private static Task<bool> CanConnectAsync(string connectionString)
    {
        string maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        return HomeworkCentral.Api.Utilities.OperationalExceptionGuard.RunAsync<bool>(
            async () =>
            {
                await using NpgsqlConnection connection = new(maintenance);
                await connection.OpenAsync();
                return true;
            },
            _ => false);
    }

    private static async Task<AppDbContext> CreateMigratedDatabaseAsync(string connectionString)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        AppDbContext db = new(options, accessScopeAccessor: null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return db;
    }
}
