using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Authorization;

public class MentionNotifyScopeTests
{
    [Theory]
    [InlineData(AccountClass.RealAccount, AccountClass.RealAccount, true)]
    [InlineData(AccountClass.RealAccount, AccountClass.DeveloperAccount, false)]
    [InlineData(AccountClass.DeveloperAccount, AccountClass.RealAccount, false)]
    [InlineData(AccountClass.DevAdmin, AccountClass.RealAccount, false)]
    [InlineData(AccountClass.DevAdmin, AccountClass.DeveloperAccount, true)]
    [InlineData(AccountClass.DeveloperAccount, AccountClass.DevAdmin, true)]
    public void CanNotify_splits_real_and_developer_traffic(
        AccountClass senderClass,
        AccountClass recipientClass,
        bool expected)
    {
        bool actual = MentionNotifyScope.CanNotify(
            senderClass,
            senderTenantDatabaseName: null,
            recipientClass,
            recipientTenantDatabaseName: null);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Developer_personas_in_same_tenant_can_notify_each_other()
    {
        Assert.True(MentionNotifyScope.CanNotify(
            AccountClass.DeveloperAccount,
            "tenant_math",
            AccountClass.DeveloperAccount,
            "tenant_math"));
    }

    [Fact]
    public void Developer_personas_in_different_tenants_cannot_notify_each_other()
    {
        Assert.False(MentionNotifyScope.CanNotify(
            AccountClass.DeveloperAccount,
            "tenant_math",
            AccountClass.DeveloperAccount,
            "tenant_science"));
    }
}
