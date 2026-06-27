namespace HomeworkCentral.Api.Models;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public short PermissionId { get; set; }

    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
