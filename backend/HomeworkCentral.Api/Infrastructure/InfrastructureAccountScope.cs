using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Infrastructure;

/// <summary>
/// Classifies infrastructure resources and users into the same real-vs-developer traffic
/// buckets used by shared chat resources.
/// </summary>
public static class InfrastructureAccountScope
{
    public static bool CanViewInfrastructure(AccessScope viewer, AccountClass ownerAccountClass) =>
        ShareableResourceVisibilityScope.CanView(viewer.AccountClass, ownerAccountClass);

    public static bool CanViewInfrastructure(AccountClass viewerAccountClass, AccountClass ownerAccountClass) =>
        ShareableResourceVisibilityScope.CanView(viewerAccountClass, ownerAccountClass);

    public static AccountClass ResolveUserAccountClass(User user)
    {
        if (string.Equals(user.Username, DevBypass.DevAdminUsername, StringComparison.OrdinalIgnoreCase))
            return AccountClass.DevAdmin;

        if (DevAccountCatalog.FindByDeveloperEmail(user.Email) is not null)
            return AccountClass.DeveloperAccount;

        return AccountClass.RealAccount;
    }

    public static AccountClass ResolveActorAccountClass(AccessScope? scope) =>
        scope?.AccountClass ?? AccountClass.RealAccount;
}
