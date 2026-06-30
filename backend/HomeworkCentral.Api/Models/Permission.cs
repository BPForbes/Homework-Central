namespace HomeworkCentral.Api.Models;

public class Permission
{
    public short PermissionId { get; set; }
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsReserved { get; set; } = false;

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
