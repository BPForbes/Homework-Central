using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatRoomGroupKeyTests
{
    [Fact]
    public void Build_scopes_groups_by_room_account_class_and_tenant()
    {
        string master = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.RealAccount, null);
        string tenant = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.DeveloperAccount, "tenant_math_fibonacci");

        Assert.Equal("chat:subject:Science:0:RealAccount:master", master);
        Assert.Equal("chat:subject:Science:0:DeveloperAccount:tenant_math_fibonacci", tenant);
        Assert.NotEqual(master, tenant);
    }

    [Fact]
    public void Build_uses_distinct_groups_per_room()
    {
        string biology = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.DeveloperAccount, "tenant_science_doc");
        string chemistry = ChatRoomGroupKey.Build("subject:Science:1", AccountClass.DeveloperAccount, "tenant_science_doc");

        Assert.NotEqual(biology, chemistry);
    }
}
