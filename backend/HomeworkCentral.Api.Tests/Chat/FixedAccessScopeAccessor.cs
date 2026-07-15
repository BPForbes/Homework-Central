using System.Security.Claims;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Chat;

internal sealed class FixedAccessScopeAccessor(AccountClass accountClass = AccountClass.RealAccount) : IAccessScopeAccessor
{
    private readonly AccessScope _scope = new(accountClass, null);

    public AccessScope? Resolve(ClaimsPrincipal user) => _scope;

    public bool CanQuery(AccountClass ownerAccountClass, string? tenantDatabaseName) =>
        ShareableResourceVisibilityScope.CanView(_scope.AccountClass, ownerAccountClass);

    public bool CanView(ClaimsPrincipal user, IScopedResource resource) =>
        ShareableResourceVisibilityScope.CanView(_scope.AccountClass, resource.OwnerAccountClass);

    public DbContextAccessScope ResolveDbContextScope() => DbContextAccessScope.Scoped(_scope);

    public AccessScope? ResolveCurrent() => _scope;
}
