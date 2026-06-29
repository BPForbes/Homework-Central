namespace HomeworkCentral.Api.DTOs;

public class DevLoginRequest
{
    public Guid DeveloperUserId { get; set; }
    public Guid? TargetUserId { get; set; }
}

public class DevLoginOptionsResponse
{
    public List<DevDeveloperOption> Developers { get; set; } = new();
}

public class DevDeveloperOption
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public List<DevUserOption> Users { get; set; } = new();
}

public class DevUserOption
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
}
