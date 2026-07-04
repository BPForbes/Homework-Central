namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Implemented by tenant-private entities subject to account-class and tenant scoping
/// (notifications, presence, audit logs, etc.). Shared community resources such as chat messages
/// use <see cref="IShareableScopedResource"/> instead.
/// </summary>
public interface IScopedResource
{
    AccountClass OwnerAccountClass { get; }
    string? TenantDatabaseName { get; }
}
