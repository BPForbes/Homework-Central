using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatRoomGroupKeyTests
{
    [Fact]
    public void Build_scopes_groups_by_room_and_real_vs_dev_bucket()
    {
        string real = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.RealAccount);
        string dev = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.DeveloperAccount);

        Assert.Equal("chat:subject:Science:0:real", real);
        Assert.Equal("chat:subject:Science:0:dev", dev);
        Assert.NotEqual(real, dev);
    }

    [Fact]
    public void Build_places_all_dev_personas_in_the_same_group_regardless_of_tenant()
    {
        string devAdmin = ChatRoomGroupKey.Build("staff:0", AccountClass.DevAdmin);
        string persona = ChatRoomGroupKey.Build("staff:0", AccountClass.DeveloperAccount);

        Assert.Equal(devAdmin, persona);
    }

    [Fact]
    public void Build_uses_distinct_groups_per_room()
    {
        string biology = ChatRoomGroupKey.Build("subject:Science:0", AccountClass.DeveloperAccount);
        string chemistry = ChatRoomGroupKey.Build("subject:Science:1", AccountClass.DeveloperAccount);

        Assert.NotEqual(biology, chemistry);
    }
}
