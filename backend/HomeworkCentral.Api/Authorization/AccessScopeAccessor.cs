using System.Security.Claims;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HomeworkCentral.Api.Authorization;

public sealed class AccessScopeAccessor(IHttpContextAccessor httpContextAccessor) : IAccessScopeAccessor
{
    public AccessScope? Resolve(ClaimsPrincipal user) => TryResolveScope(user);

    public bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return true;

        ClaimsPrincipal user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return false;

        AccessScope? scope = TryResolveScope(user);
        if (scope is null)
            return false;

        return ResourceVisibilityScope.CanView(scope, ownerAccountClass, tenantDatabaseName);
    }

    public bool CanView(ClaimsPrincipal user, IScopedResource resource)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        AccessScope? scope = TryResolveScope(user);
        return scope is not null && ResourceVisibilityScope.CanView(scope, resource);
    }

    public DbContextAccessScope ResolveDbContextScope()
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return DbContextAccessScope.Unrestricted();

        ClaimsPrincipal user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return DbContextAccessScope.Denied();

        AccessScope? scope = TryResolveScope(user);
        return scope is null
            ? DbContextAccessScope.Denied()
            : DbContextAccessScope.Scoped(scope);
    }

    public AccessScope? ResolveCurrent()
    {
        DbContextAccessScope scope = ResolveDbContextScope();
        return scope.IsAuthenticated ? scope.Scope : null;
    }

    private static AccessScope? TryResolveScope(ClaimsPrincipal user)
    {
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (string.IsNullOrWhiteSpace(accountClassClaim)
            || !Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass accountClass))
        {
            return null;
        }

        string? tenantDatabaseName = user.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
        return new AccessScope(accountClass, string.IsNullOrWhiteSpace(tenantDatabaseName) ? null : tenantDatabaseName);
    }
}
