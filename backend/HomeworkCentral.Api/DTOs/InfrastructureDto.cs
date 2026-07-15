namespace HomeworkCentral.Api.DTOs;

public class CustomRoleDto
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string? ClaimHostRoomId { get; set; }
    public string? MessageColor { get; set; }
    public bool IsMentionableByUsers { get; set; }
    public List<short> PermissionIds { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
}

public class RoleAppearanceDto
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsCustom { get; set; }
    public string MessageColor { get; set; } = null!;
    public bool IsMentionableByUsers { get; set; }
}

public class UpdateRoleAppearanceRequest
{
    public string? MessageColor { get; set; }
    public bool? IsMentionableByUsers { get; set; }
}

public class MentionRoleOptionDto
{
    public string Name { get; set; } = null!;
    public string MessageColor { get; set; } = null!;
    public bool IsCustom { get; set; }
}

public class CreateCustomRoleRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public List<short> PermissionIds { get; set; } = [];
}

public class UpdateCustomRoleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public List<short>? PermissionIds { get; set; }
}

public class SetRoleClaimPlacementRequest
{
    public string? ClaimHostRoomId { get; set; }
    public string? Password { get; set; }
}

public class ReorderClaimRolesRequest
{
    public List<Guid> OrderedRoleIds { get; set; } = [];
}

public class InfoEntryDto
{
    public Guid EntryId { get; set; }
    public Guid ChannelId { get; set; }
    public string AuthorUsername { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool CanEdit { get; set; }
}

public class InfoEntryFeedDto
{
    public List<InfoEntryDto> Entries { get; set; } = [];
    /// <summary>Whether the caller can add a new entry right now (new entries are never subject to the edit-window lock).</summary>
    public bool CanCreate { get; set; }
}

public class CreateInfoEntryRequest
{
    public string Content { get; set; } = null!;
}

public class UpdateInfoEntryRequest
{
    public string Content { get; set; } = null!;
}

public class CustomChannelAccessRuleDto
{
    public Guid? CustomRoleId { get; set; }
    public string? CustomRoleName { get; set; }
    public short? PlatformRoleBit { get; set; }
    public string? PlatformRoleName { get; set; }
}

public class CustomChannelDto
{
    public Guid ChannelId { get; set; }
    public string RoomId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? IconName { get; set; }
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public string RoomType { get; set; } = null!;
    public bool IsPrivate { get; set; }
    public string? InfoContent { get; set; }
    public string TieType { get; set; } = null!;
    public string? TieSubjectMask { get; set; }
    public short? TieSubjectBitIndex { get; set; }
    public short? TiePlatformRoleBit { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<CustomChannelAccessRuleDto> AccessRules { get; set; } = [];
    public bool CanEditInfo { get; set; }
}

public class CreateCustomChannelRequest
{
    public string DisplayName { get; set; } = null!;
    public string? IconName { get; set; }
    public string CategoryKey { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = null!;
    public string RoomType { get; set; } = null!;
    public bool IsPrivate { get; set; }
    public string? InfoContent { get; set; }
    public string TieType { get; set; } = "None";
    public string? TieSubjectMask { get; set; }
    public short? TieSubjectBitIndex { get; set; }
    public short? TiePlatformRoleBit { get; set; }
    public List<CustomChannelAccessRuleInput> AccessRules { get; set; } = [];
    public string? Password { get; set; }
}

public class UpdateCustomChannelRequest
{
    public string? DisplayName { get; set; }
    public string? IconName { get; set; }
    public string? CategoryKey { get; set; }
    public string? CategoryDisplayName { get; set; }
    public bool? IsPrivate { get; set; }
    public string? InfoContent { get; set; }
    public string? TieType { get; set; }
    public string? TieSubjectMask { get; set; }
    public short? TieSubjectBitIndex { get; set; }
    public short? TiePlatformRoleBit { get; set; }
    public List<CustomChannelAccessRuleInput>? AccessRules { get; set; }
    public string? Password { get; set; }
}

public class CustomChannelAccessRuleInput
{
    public Guid? CustomRoleId { get; set; }
    public short? PlatformRoleBit { get; set; }
}

public class ClaimableCustomRoleDto
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public bool Claimed { get; set; }
}

public class PasswordConfirmRequest
{
    public string Password { get; set; } = null!;
}

public class ModerationRiskWarningDto
{
    public bool RequiresPassword { get; set; }
    public List<string> RiskyPermissions { get; set; } = [];
}

public class InfrastructureUserLookupDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? TenantDatabaseName { get; set; }
    public short HighestPlatformRoleBit { get; set; }
    public string HighestPlatformRoleName { get; set; } = null!;
    public List<CustomRoleDto> CustomRoles { get; set; } = [];
    public List<PlatformRoleAssignmentDto> PlatformRoles { get; set; } = [];
    public List<short> EffectivePermissionIds { get; set; } = [];
}

public class PlatformRoleAssignmentDto
{
    public string Name { get; set; } = null!;
    public short Bit { get; set; }
    public bool IsAssigned { get; set; }
    public bool CanGrant { get; set; }
    public bool CanRevoke { get; set; }
}

public class AssignableUserDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? TenantDatabaseName { get; set; }
    public short HighestPlatformRoleBit { get; set; }
    public string HighestPlatformRoleName { get; set; } = null!;
    public bool AlreadyAssigned { get; set; }
    public bool CanAssign { get; set; }
}

public class BulkAssignCustomRoleRequest
{
    public List<BulkAssignUserTarget> Users { get; set; } = [];
}

public class BulkAssignUserTarget
{
    public Guid UserId { get; set; }
    public string? TenantDatabaseName { get; set; }
}

public class AssignCustomRoleRequest
{
    public string? Password { get; set; }
}
