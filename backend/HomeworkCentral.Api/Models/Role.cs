using System.Collections;

namespace HomeworkCentral.Api.Models;

public class Role
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public BitArray RoleMask { get; set; } = new(64);
    public BitArray PermissionMask { get; set; } = new(256);
    public BitArray FeatureMask { get; set; } = new(256);

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
