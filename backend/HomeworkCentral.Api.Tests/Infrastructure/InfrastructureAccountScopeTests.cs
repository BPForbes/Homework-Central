using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Tests.Infrastructure;

public class InfrastructureAccountScopeTests
{
    [Fact]
    public void Real_viewer_cannot_see_dev_custom_channel()
    {
        AccessScope realViewer = new(AccountClass.RealAccount, null);
        CustomChannel devChannel = new() { OwnerAccountClass = AccountClass.DeveloperAccount };

        Assert.False(InfrastructureAccountScope.CanViewInfrastructure(realViewer, devChannel.OwnerAccountClass));
    }

    [Fact]
    public void Dev_viewer_cannot_see_real_custom_channel()
    {
        AccessScope devViewer = new(AccountClass.DeveloperAccount, "tenant_math");
        CustomChannel realChannel = new() { OwnerAccountClass = AccountClass.RealAccount };

        Assert.False(InfrastructureAccountScope.CanViewInfrastructure(devViewer, realChannel.OwnerAccountClass));
    }

    [Fact]
    public void DevAdmin_and_developer_accounts_share_infrastructure_scope()
    {
        AccessScope devAdmin = new(AccountClass.DevAdmin, null);
        AccountClass developerRoleOwner = AccountClass.DeveloperAccount;

        Assert.True(InfrastructureAccountScope.CanViewInfrastructure(devAdmin, developerRoleOwner));
    }
}
