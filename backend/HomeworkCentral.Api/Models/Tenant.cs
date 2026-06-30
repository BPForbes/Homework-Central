namespace HomeworkCentral.Api.Models;

/// <summary>Registry entry mapping a persona to an isolated tenant database.</summary>
public class Tenant
{
    public Guid TenantId { get; set; }
    public string DatabaseName { get; set; } = null!;
    public string ClusterSlug { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string DeveloperEmail { get; set; } = null!;
    public string PersonaEmail { get; set; } = null!;
    public string ClusterEnvironment { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
