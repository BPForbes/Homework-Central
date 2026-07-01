using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HomeworkCentral.Api.Tests.Authorization;

public class AccessScopeAccessorTests
{
    private readonly HttpContextAccessor _httpContextAccessor = new();
    private readonly AccessScopeAccessor _accessor;

    public AccessScopeAccessorTests()
    {
        _accessor = new AccessScopeAccessor(_httpContextAccessor);
    }

    [Theory]
    [InlineData(AccountClass.RealAccount, "tenant_math", AccountClass.RealAccount, "tenant_math", true)]
    [InlineData(AccountClass.RealAccount, "tenant_math", AccountClass.RealAccount, "tenant_science", false)]
    [InlineData(AccountClass.RealAccount, "tenant_math", AccountClass.DeveloperAccount, "tenant_math", false)]
    [InlineData(AccountClass.DeveloperAccount, "tenant_math", AccountClass.DeveloperAccount, "tenant_math", true)]
    [InlineData(AccountClass.DeveloperAccount, "tenant_math", AccountClass.DeveloperAccount, "tenant_science", false)]
    [InlineData(AccountClass.DevAdmin, null, AccountClass.DeveloperAccount, "tenant_math", true)]
    [InlineData(AccountClass.DevAdmin, null, AccountClass.RealAccount, "production", false)]
    public void CanQuery_enforces_account_class_and_tenant_matrix(
        AccountClass viewerClass,
        string? viewerTenant,
        AccountClass ownerClass,
        string? ownerTenant,
        bool expected)
    {
        SetUser(viewerClass, viewerTenant);

        bool actual = _accessor.CanQuery(ownerClass, ownerTenant);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_parses_account_class_and_tenant_claims()
    {
        ClaimsPrincipal user = CreatePrincipal(AccountClass.DeveloperAccount, "tenant_cs");

        AccessScope? scope = _accessor.Resolve(user);

        Assert.NotNull(scope);
        Assert.Equal(AccountClass.DeveloperAccount, scope.AccountClass);
        Assert.Equal("tenant_cs", scope.TenantDatabaseName);
    }

    [Fact]
    public void Resolve_returns_null_when_claim_missing()
    {
        AccessScope? scope = _accessor.Resolve(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(scope);
    }

    [Fact]
    public void CanQuery_denies_when_http_context_has_unauthenticated_user()
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        Assert.False(_accessor.CanQuery(AccountClass.RealAccount, "tenant_math"));
    }

    [Fact]
    public void CanQuery_denies_when_account_class_claim_is_invalid()
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(TenancyConstants.AccountClassClaimName, "NotARealClass"),
            ],
            authenticationType: "Test")),
        };

        Assert.False(_accessor.CanQuery(AccountClass.RealAccount, "tenant_math"));
    }

    [Fact]
    public void ResolveDbContextScope_is_unrestricted_without_http_context()
    {
        DbContextAccessScope scope = _accessor.ResolveDbContextScope();

        Assert.True(scope.BypassFilters);
        Assert.False(scope.IsAuthenticated);
    }

    [Fact]
    public void ResolveDbContextScope_denies_unauthenticated_request()
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };

        DbContextAccessScope scope = _accessor.ResolveDbContextScope();

        Assert.False(scope.BypassFilters);
        Assert.False(scope.IsAuthenticated);
    }

    private void SetUser(AccountClass accountClass, string? tenantDatabaseName)
    {
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = CreatePrincipal(accountClass, tenantDatabaseName),
        };
    }

    private static ClaimsPrincipal CreatePrincipal(AccountClass accountClass, string? tenantDatabaseName)
    {
        List<Claim> claims =
        [
            new(TenancyConstants.AccountClassClaimName, accountClass.ToString()),
        ];

        if (!string.IsNullOrWhiteSpace(tenantDatabaseName))
            claims.Add(new Claim(TenancyConstants.TenantDbClaimName, tenantDatabaseName));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }
}
