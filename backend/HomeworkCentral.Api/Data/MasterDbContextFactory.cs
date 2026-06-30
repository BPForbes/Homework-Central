using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeworkCentral.Api.Data;

public class MasterDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public MasterDbContext CreateDbContext(string[] args)
    {
        string port = Environment.GetEnvironmentVariable("POSTGRES_HOST_PORT") ?? "5434";
        DbContextOptions<MasterDbContext> opts = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql($"Host=localhost;Port={port};Database=homework_central_master;Username=postgres;Password=postgres")
            .Options;
        return new MasterDbContext(opts);
    }
}
