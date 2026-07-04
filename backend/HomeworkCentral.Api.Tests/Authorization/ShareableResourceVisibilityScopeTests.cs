using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Authorization;

public class ShareableResourceVisibilityScopeTests
{
    [Theory]
    [InlineData(AccountClass.RealAccount, AccountClass.RealAccount, true)]
    [InlineData(AccountClass.RealAccount, AccountClass.DeveloperAccount, false)]
    [InlineData(AccountClass.DeveloperAccount, AccountClass.DeveloperAccount, true)]
    [InlineData(AccountClass.DeveloperAccount, AccountClass.RealAccount, false)]
    [InlineData(AccountClass.DevAdmin, AccountClass.DeveloperAccount, true)]
    [InlineData(AccountClass.DevAdmin, AccountClass.RealAccount, false)]
    public void CanView_splits_real_and_developer_traffic(
        AccountClass viewerClass,
        AccountClass messageClass,
        bool expected)
    {
        bool actual = ShareableResourceVisibilityScope.CanView(viewerClass, messageClass);
        Assert.Equal(expected, actual);
    }
}
