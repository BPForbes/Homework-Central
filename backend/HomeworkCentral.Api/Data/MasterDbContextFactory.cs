using HomeworkCentral.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeworkCentral.Api.Data;

public class MasterDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public MasterDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<MasterDbContext> opts = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(DesignTimeConnection.GetMasterConnection(), npgsql =>
                npgsql.MigrationsHistoryTable(TenancyConstants.MasterMigrationsHistoryTable))
            .Options;
        return new MasterDbContext(opts);
    }
}
