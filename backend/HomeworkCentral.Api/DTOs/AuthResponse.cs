namespace HomeworkCentral.Api.DTOs;

public class AuthResponse
{
    public string AccessToken { get; set; } = null!;
    public int ExpiresIn { get; set; }
    public UserDto User { get; set; } = null!;
}

public class UserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public List<string> Roles { get; set; } = [];
    public string AccountClass { get; set; } = null!;
    public string PermissionMask { get; set; } = null!;
    public string RoleMask { get; set; } = null!;
    public string FeatureMask { get; set; } = null!;
    public string GeneralSubjectMask { get; set; } = null!;
    public Dictionary<string, string> SubjectExpertiseMasks { get; set; } = [];
    public string StatusMask { get; set; } = null!;
}

public class EffectiveMaskDto
{
    public string RoleMask { get; set; } = null!;
    public string ModerationMask { get; set; } = null!;
    public string FeatureMask { get; set; } = null!;
    public string GeneralSubjectMask { get; set; } = null!;
    public Dictionary<string, string> SubjectExpertiseMasks { get; set; } = [];
    public string StatusMask { get; set; } = null!;
    public List<Guid> CustomRoleIds { get; set; } = [];
}
