using System.Security.Claims;

namespace HomeworkCentral.Api.Authorization;

public interface IAccessScopeAccessor
{
    AccessScope Resolve(ClaimsPrincipal user);

    bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName);

    bool CanView(ClaimsPrincipal user, IScopedResource resource);

    /// <summary>
    /// Resolves the scope of the current ambient HTTP request (via the accessor's own
    /// <c>IHttpContextAccessor</c>), or null if there is no authenticated request in scope
    /// (background job, unauthenticated caller, etc.). Intended to be called once per
    /// <c>AppDbContext</c> construction so its result can be captured as plain scalar values —
    /// see <see cref="HomeworkCentral.Api.Data.ScopedResourceQueryFilterExtensions"/> for why
    /// EF global query filters must not call back into this accessor's own methods directly.
    /// </summary>
    AccessScope? ResolveCurrent();
}
