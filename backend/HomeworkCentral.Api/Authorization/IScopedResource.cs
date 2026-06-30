namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Implemented by any entity/DTO subject to account-class and tenant scoping
/// (chat messages, channels, notifications, presence, audit logs, etc.).
/// </summary>
public interface IScopedResource
{
    AccountClass OwnerAccountClass { get; }
    string? TenantDatabaseName { get; }
}
