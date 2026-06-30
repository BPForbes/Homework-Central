using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeworkCentral.Api.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string port = Environment.GetEnvironmentVariable("POSTGRES_HOST_PORT") ?? "5434";
        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"Host=localhost;Port={port};Database=homework_central_master;Username=postgres;Password=postgres", npgsql =>
                npgsql.MigrationsHistoryTable(TenancyConstants.AppMigrationsHistoryTable))
            .Options;
        return new AppDbContext(opts);
    }
}
