using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeworkCentral.Api.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DesignTimeConnection.GetMasterConnection(), npgsql =>
                npgsql.MigrationsHistoryTable(TenancyConstants.AppMigrationsHistoryTable))
            .Options;
        return new AppDbContext(opts);
    }
}
