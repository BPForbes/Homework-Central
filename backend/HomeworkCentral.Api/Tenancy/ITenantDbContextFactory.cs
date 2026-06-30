using HomeworkCentral.Api.Data;

namespace HomeworkCentral.Api.Tenancy;

public interface ITenantDbContextFactory
{
    AppDbContext Create(string databaseName);
    Task<AppDbContext> CreateForDeveloperEmailAsync(string developerEmail, CancellationToken ct = default);
}
