using System.Collections;

namespace HomeworkCentral.Api.Models;

public class Role
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = null!;
    public BitArray PermissionMask { get; set; } = new BitArray(256);
    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
