using Ganss.Xss;

namespace HomeworkCentral.Api.Security;

/// <summary>
/// Strips unsafe HTML (script tags, event handlers, javascript: URLs, etc.) from user-submitted
/// content before it's persisted/rendered. Registered as a singleton in DI: <see cref="HtmlSanitizer"/>'s
/// allow-lists are configured once here and never mutated afterward, so <see cref="Sanitize"/> is
/// safe to call concurrently from multiple requests against the same instance.
/// </summary>
public sealed class HtmlContentSanitizer : IContentSanitizer
{
    private readonly HtmlSanitizer _sanitizer = new();

    public string Sanitize(string rawContent) => _sanitizer.Sanitize(rawContent);
}
