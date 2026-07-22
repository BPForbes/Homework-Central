namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// TTL for signed attachment download tokens minted by
/// <see cref="IAttachmentAccessTokenService"/>. Bound from configuration section
/// <c>AttachmentAccess</c>.
/// </summary>
public class AttachmentAccessOptions
{
    /// <summary>Minutes until a signed download URL expires (default 60).</summary>
    public int TokenTtlMinutes { get; set; } = 60;
}
