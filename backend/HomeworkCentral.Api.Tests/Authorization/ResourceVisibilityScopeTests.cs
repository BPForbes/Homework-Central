using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Authorization;

public class ResourceVisibilityScopeTests
{
    [Theory]
    [InlineData(AccountClass.RealAccount, "tenant_a", AccountClass.RealAccount, "tenant_a", true)]
    [InlineData(AccountClass.RealAccount, "tenant_a", AccountClass.RealAccount, "tenant_b", false)]
    [InlineData(AccountClass.DeveloperAccount, "tenant_a", AccountClass.DeveloperAccount, "tenant_a", true)]
    [InlineData(AccountClass.DevAdmin, null, AccountClass.DeveloperAccount, "tenant_b", true)]
    [InlineData(AccountClass.DevAdmin, null, AccountClass.RealAccount, null, false)]
    public void CanView_matches_handler_rules(
        AccountClass viewerClass,
        string? viewerTenant,
        AccountClass ownerClass,
        string? ownerTenant,
        bool expected)
    {
        AccessScope viewer = new(viewerClass, viewerTenant);
        TestScopedResource resource = new(ownerClass, ownerTenant);

        bool actual = ResourceVisibilityScope.CanView(viewer, resource);

        Assert.Equal(expected, actual);
    }

    private sealed class TestScopedResource(AccountClass ownerAccountClass, string? tenantDatabaseName) : IScopedResource
    {
        public AccountClass OwnerAccountClass { get; } = ownerAccountClass;
        public string? TenantDatabaseName { get; } = tenantDatabaseName;
    }
}
