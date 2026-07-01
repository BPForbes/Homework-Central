using System.Security.Claims;

namespace HomeworkCentral.Api.Authorization;

public interface IAccessScopeAccessor
{
    AccessScope? Resolve(ClaimsPrincipal user);

    bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName);

    bool CanView(ClaimsPrincipal user, IScopedResource resource);

    /// <summary>
    /// Resolves how scoped EF query filters should behave for the current ambient context.
    /// Returns unrestricted when there is no HTTP request (migrations, seed jobs), denied when
    /// a request is present but lacks a valid authenticated scope, and scoped otherwise.
    /// </summary>
    DbContextAccessScope ResolveDbContextScope();

    /// <summary>
    /// Resolves the scope of the current ambient HTTP request (via the accessor's own
    /// <c>IHttpContextAccessor</c>), or null if there is no valid authenticated scope.
    /// Intended to be called once per <c>AppDbContext</c> construction so its result can be
    /// captured as plain scalar values — see
    /// <see cref="HomeworkCentral.Api.Data.ScopedResourceQueryFilterExtensions"/> for why EF
    /// global query filters must not call back into this accessor's own methods directly.
    /// </summary>
    AccessScope? ResolveCurrent();
}
