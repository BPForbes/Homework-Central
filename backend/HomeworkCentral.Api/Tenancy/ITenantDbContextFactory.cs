using HomeworkCentral.Api.Data;

namespace HomeworkCentral.Api.Tenancy;

public interface ITenantDbContextFactory
{
    Task<AppDbContext> CreateForRegisteredTenantAsync(string databaseName, CancellationToken ct = default);
}
