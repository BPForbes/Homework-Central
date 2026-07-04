using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatRoomCatalogTests
{
    [Theory]
    [InlineData("C++")]
    [InlineData("C#")]
    [InlineData("CSS")]
    [InlineData("APIs")]
    [InlineData("PostgreSQL")]
    [InlineData("MySQL")]
    public void Computer_science_rooms_use_friendly_display_names(string expectedDisplayName)
    {
        Assert.Contains(
            ChatRoomCatalog.SubjectRooms,
            room => room.ExpertiseCategory == SubjectMaskNames.ComputerScience
                && room.RoomDisplayName == expectedDisplayName);
    }
}
