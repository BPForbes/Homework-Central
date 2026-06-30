namespace HomeworkCentral.Api.DTOs;

/// <summary>Developer bypass login payload: pick a developer, optionally impersonate a persona.</summary>
public class DevLoginRequest
{
    /// <summary>User id of the selected developer account (must have Developer role).</summary>
    public Guid DeveloperUserId { get; set; }

    /// <summary>Optional persona to impersonate. When omitted, global DevAdmin (Owner) on master is used.</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>Registered persona database name. Required when impersonating a persona.</summary>
    public string? TenantDatabaseName { get; set; }
}

/// <summary>Dropdown data for /devlogin.</summary>
public class DevLoginOptionsResponse
{
    public List<DevDeveloperOption> Developers { get; set; } = [];
}

/// <summary>A subject-area developer account with its scoped persona list.</summary>
public class DevDeveloperOption
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public List<DevUserOption> Users { get; set; } = [];
}

/// <summary>A persona that can be impersonated under a developer account.</summary>
public class DevUserOption
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public string TenantDatabaseName { get; set; } = null!;
}
