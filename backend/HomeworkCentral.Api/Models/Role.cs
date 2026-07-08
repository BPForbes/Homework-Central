using System.Collections;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Models;

public class Role
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsCustom { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AccountClass OwnerAccountClass { get; set; }
    /// <summary>Room id where this custom role can be claimed (e.g. general:get-roles or custom:guid).</summary>
    public string? ClaimHostRoomId { get; set; }
    /// <summary>Font Awesome icon id, e.g. fas:shield-halved.</summary>
    public string? IconName { get; set; }
    /// <summary>Hex color (#RRGGBB) used for chat messages and @mentions for this role.</summary>
    public string? MessageColor { get; set; }
    /// <summary>When true, standard users may @mention this role in chat.</summary>
    public bool IsMentionableByUsers { get; set; }
    public BitArray RoleMask { get; set; } = new(64);
    public BitArray PermissionMask { get; set; } = new(256);
    public BitArray FeatureMask { get; set; } = new(256);

    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
