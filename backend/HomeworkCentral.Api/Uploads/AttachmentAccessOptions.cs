namespace HomeworkCentral.Api.Uploads;

/// <summary>TTL for signed download tokens (config <c>AttachmentAccess</c>).</summary>
public class AttachmentAccessOptions
{
    public int TokenTtlMinutes { get; set; } = 60;
}
