using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HomeworkCentral.Api.Data;

/// <summary>Creates databases and applies migrations with retries for Docker warm-up.</summary>
public static class DatabaseStartup
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task InitializeDevelopmentAsync(
        IServiceProvider services,
        CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await InitializeOnceAsync(services, ct);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
            {
                ILogger<Program>? logger = services.GetService<ILogger<Program>>();
                logger?.LogWarning(
                    ex,
                    "Database startup attempt {Attempt}/{MaxAttempts} failed; retrying in {DelaySeconds}s",
                    attempt,
                    MaxAttempts,
                    RetryDelay.TotalSeconds);

                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private static async Task InitializeOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        using IServiceScope scope = services.CreateScope();
        ITenantConnectionResolver connectionResolver =
            scope.ServiceProvider.GetRequiredService<ITenantConnectionResolver>();

        await TenantDatabaseProvisioner.EnsureMasterDatabaseExistsAsync(connectionResolver, ct);

        AppDbContext appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.MigrateAsync(ct);

        MasterDbContext masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        await masterDb.Database.MigrateAsync(ct);
    }

    private static bool IsTransient(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
            {
                // Connection failures, too many connections, admin shutdown.
                if (pg.SqlState is "08000" or "08001" or "08006" or "57P03" or "53300")
                    return true;

                // Database is still being created or not yet visible.
                if (pg.SqlState is "3D000")
                    return true;
            }

            if (current is NpgsqlException or TimeoutException or IOException)
                return true;
        }

        return false;
    }
}
