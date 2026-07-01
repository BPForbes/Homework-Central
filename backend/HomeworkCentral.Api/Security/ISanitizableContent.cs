namespace HomeworkCentral.Api.Security;

/// <summary>
/// Free-text user content persisted and rendered to other users (chat, bios, etc.).
/// </summary>
public interface ISanitizableContent
{
    string RawContent { get; }
    string? SanitizedContent { get; set; }
}
