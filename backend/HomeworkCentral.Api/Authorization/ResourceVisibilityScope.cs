namespace HomeworkCentral.Api.Authorization;

/// <summary>Shared account-class and tenant visibility rules for handlers and EF query filters.</summary>
public static class ResourceVisibilityScope
{
    public static bool CanView(AccessScope viewer, AccountClass ownerAccountClass, string? tenantDatabaseName) =>
        viewer.AccountClass switch
        {
            AccountClass.RealAccount => ownerAccountClass == AccountClass.RealAccount
                && tenantDatabaseName == viewer.TenantDatabaseName,
            AccountClass.DevAdmin => ownerAccountClass != AccountClass.RealAccount,
            AccountClass.DeveloperAccount => ownerAccountClass == AccountClass.DeveloperAccount
                && tenantDatabaseName == viewer.TenantDatabaseName,
            _ => false,
        };

    public static bool CanView(AccessScope viewer, IScopedResource resource) =>
        CanView(viewer, resource.OwnerAccountClass, resource.TenantDatabaseName);
}
