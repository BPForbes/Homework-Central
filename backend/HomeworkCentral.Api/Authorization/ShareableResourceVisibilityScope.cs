namespace HomeworkCentral.Api.Authorization;

/// <summary>
/// Visibility rules for shared community resources (<see cref="IShareableScopedResource"/>).
/// Splits real production traffic from developer/test traffic without per-tenant matching.
/// </summary>
public static class ShareableResourceVisibilityScope
{
    public static bool CanView(AccountClass viewerAccountClass, AccountClass ownerAccountClass) =>
        (viewerAccountClass == AccountClass.RealAccount && ownerAccountClass == AccountClass.RealAccount)
        || (viewerAccountClass != AccountClass.RealAccount && ownerAccountClass != AccountClass.RealAccount);
}
