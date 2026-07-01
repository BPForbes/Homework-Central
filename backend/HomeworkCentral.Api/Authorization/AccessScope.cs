namespace HomeworkCentral.Api.Authorization;

public sealed record AccessScope(AccountClass AccountClass, string? TenantDatabaseName);
