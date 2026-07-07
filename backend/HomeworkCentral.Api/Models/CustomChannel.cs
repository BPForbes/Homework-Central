using HomeworkCentral.Api.Infrastructure;

namespace HomeworkCentral.Api.Models;

public class CustomChannel
{
    public Guid ChannelId { get; set; }
    public string RoomId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public CustomRoomType RoomType { get; set; }
    public bool IsPrivate { get; set; }
    public string? InfoContent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public ChannelTieType TieType { get; set; }
    public string? TieSubjectMask { get; set; }
    public short? TieSubjectBitIndex { get; set; }
    public short? TiePlatformRoleBit { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<CustomChannelAccessRule> AccessRules { get; set; } = [];
}

public class CustomChannelAccessRule
{
    public Guid AccessRuleId { get; set; }
    public Guid ChannelId { get; set; }
    public CustomChannel Channel { get; set; } = null!;
    public Guid? CustomRoleId { get; set; }
    public Role? CustomRole { get; set; }
    public short? PlatformRoleBit { get; set; }
}
