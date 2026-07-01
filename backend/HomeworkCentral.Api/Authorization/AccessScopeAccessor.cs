using System.Security.Claims;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HomeworkCentral.Api.Authorization;

public sealed class AccessScopeAccessor(IHttpContextAccessor httpContextAccessor) : IAccessScopeAccessor
{
    public AccessScope Resolve(ClaimsPrincipal user)
    {
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (string.IsNullOrWhiteSpace(accountClassClaim)
            || !Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass accountClass))
        {
            accountClass = AccountClass.RealAccount;
        }

        string? tenantDatabaseName = user.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
        return new AccessScope(accountClass, string.IsNullOrWhiteSpace(tenantDatabaseName) ? null : tenantDatabaseName);
    }

    public bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user is null || user.Identity?.IsAuthenticated != true)
            return true;

        return ResourceVisibilityScope.CanView(Resolve(user), ownerAccountClass, tenantDatabaseName);
    }

    public bool CanView(ClaimsPrincipal user, IScopedResource resource) =>
        ResourceVisibilityScope.CanView(Resolve(user), resource);

    public AccessScope? ResolveCurrent()
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        if (user is null || user.Identity?.IsAuthenticated != true)
            return null;

        return Resolve(user);
    }
}
