namespace HomeworkCentral.Api.DTOs;

public class GrantRoleRequest
{
    public Guid UserId { get; set; }
    public string RoleName { get; set; } = null!;
}

public class RevokeRoleRequest
{
    public Guid UserId { get; set; }
    public string RoleName { get; set; } = null!;
}

public class VerifyUserRequest
{
    public Guid UserId { get; set; }
}
