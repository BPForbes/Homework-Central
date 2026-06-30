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

        AccessScope scope = _accessor.Resolve(user);

        Assert.Equal(AccountClass.DeveloperAccount, scope.AccountClass);
        Assert.Equal("tenant_cs", scope.TenantDatabaseName);
    }

    [Fact]
    public void Resolve_defaults_to_real_account_when_claim_missing()
    {
        AccessScope scope = _accessor.Resolve(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(AccountClass.RealAccount, scope.AccountClass);
        Assert.Null(scope.TenantDatabaseName);
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
