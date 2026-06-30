using System.Security.Claims;

namespace HomeworkCentral.Api.Authorization;

public interface IAccessScopeAccessor
{
    AccessScope Resolve(ClaimsPrincipal user);

    bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName);

    bool CanView(ClaimsPrincipal user, IScopedResource resource);
}
