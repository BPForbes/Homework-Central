using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// The checkpoint table is append-only in normal operation and each row carries a full packed
/// parameter snapshot, so an unbounded lineage is a permanent disk cost that continuous training
/// adds to on every publish. These cover the retention bound: the newest generations survive, the
/// tail is deleted, and the trim commits with the publish rather than ahead of it.
/// </summary>
public class NeuralNetCheckpointStoreTrimTests
{
    /// <summary>
    /// These tests call <c>EnsureDeletedAsync</c>, so they must never be pointed at a database
    /// another suite is using. The host and credentials are borrowed from whatever Postgres the
    /// build already has, but the database name is always forced to this one.
    /// </summary>
    private const string IsolatedDatabaseName = "homework_central_test_checkpoints";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private static string ResolveConnectionString()
    {
        string baseConnectionString =
            Environment.GetEnvironmentVariable("TEST_CHECKPOINT_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("TEST_DATABASE_URL")
            ?? DefaultConnectionString;

        return new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = IsolatedDatabaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// Probes the server via its <c>postgres</c> maintenance database rather than the isolated one
    /// — which does not exist until <c>MigrateAsync</c> creates it, so probing it directly would
    /// skip every test on a machine that does have Postgres.
    ///
    /// Failures go through <see cref="OperationalExceptionGuard"/> rather than a bare
    /// <c>catch</c>: "no server here, skip the test" should only be concluded from an
    /// infrastructure failure, and a bug in the probe itself must still surface as a test error.
    /// </summary>
    private static Task<bool> CanConnectAsync(string connectionString)
    {
        string maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;

        return OperationalExceptionGuard.RunAsync<bool>(
            async () =>
            {
                await using NpgsqlConnection connection = new(maintenance);
                await connection.OpenAsync();
                return true;
            },
            _ => false);
    }

    [SkippableFact]
    public async Task Publishing_past_the_retention_bound_drops_only_the_oldest_generations()
    {
        string connectionString = ResolveConnectionString();
        Skip.IfNot(await CanConnectAsync(connectionString), "Requires a reachable Postgres server.");

        await using AppDbContext db = await CreateMigratedDatabaseAsync(connectionString);
        NeuralNetCheckpointStore store = new(db);

        int published = NeuralNetCheckpointStore.RetainedGenerations + 3;
        for (int i = 0; i < published; i++)
        {
            await store.PublishAsync(
                NeuralModelKindChatMonitoring.Moderation,
                $"v{i}",
                Snapshot($"weights-{i}"),
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        List<long> generations = await db.NeuralNetCanonicalCheckpoints
            .AsNoTracking()
            .Where(x => x.ChatMonitoringKind == NeuralModelKindChatMonitoring.Moderation)
            .Select(x => x.Generation)
            .OrderBy(g => g)
            .ToListAsync();

        Assert.Equal(NeuralNetCheckpointStore.RetainedGenerations, generations.Count);
        // Generations are 1-based, so publishing 13 with a bound of 10 leaves 4..13.
        Assert.Equal(published - NeuralNetCheckpointStore.RetainedGenerations + 1, generations[0]);
        Assert.Equal(published, generations[^1]);
    }

    [SkippableFact]
    public async Task The_newest_generation_is_still_the_one_read_back_after_trimming()
    {
        string connectionString = ResolveConnectionString();
        Skip.IfNot(await CanConnectAsync(connectionString), "Requires a reachable Postgres server.");

        await using AppDbContext db = await CreateMigratedDatabaseAsync(connectionString);
        NeuralNetCheckpointStore store = new(db);

        int published = NeuralNetCheckpointStore.RetainedGenerations + 2;
        for (int i = 0; i < published; i++)
        {
            await store.PublishAsync(
                NeuralModelKindChatMonitoring.Moderation,
                $"v{i}",
                Snapshot($"weights-{i}"),
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        NeuralNetCanonicalCheckpoint? current =
            await store.GetCurrentAsync(NeuralModelKindChatMonitoring.Moderation, CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(published, current!.Generation);
        Assert.Equal($"weights-{published - 1}", current.ParametersBase64);
    }

    [SkippableFact]
    public async Task Trimming_is_scoped_to_one_lineage()
    {
        string connectionString = ResolveConnectionString();
        Skip.IfNot(await CanConnectAsync(connectionString), "Requires a reachable Postgres server.");

        await using AppDbContext db = await CreateMigratedDatabaseAsync(connectionString);
        NeuralNetCheckpointStore store = new(db);

        await store.PublishAsync(
            NeuralModelKindChatMonitoring.Tutoring, "tutoring-v1", Snapshot("tutoring"), CancellationToken.None);
        await db.SaveChangesAsync();

        for (int i = 0; i < NeuralNetCheckpointStore.RetainedGenerations + 5; i++)
        {
            await store.PublishAsync(
                NeuralModelKindChatMonitoring.Moderation, $"v{i}", Snapshot($"weights-{i}"), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        // The Tutoring lineage has one generation and is nowhere near the bound, so a Moderation
        // trim must leave it completely alone.
        int tutoring = await db.NeuralNetCanonicalCheckpoints
            .AsNoTracking()
            .CountAsync(x => x.ChatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring);

        Assert.Equal(1, tutoring);
    }

    private static NeuralNetParameterSnapshot Snapshot(string packed) =>
        new(
            CanonicalGeneration: null,
            LocalRevision: 1,
            NumericFormat: "f32",
            Encoding: "base64",
            ParameterCount: 1,
            PackedValues: packed,
            Checksum: $"sum-{packed}");

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
