using Ganss.Xss;

namespace HomeworkCentral.Api.Security;

public sealed class HtmlContentSanitizer : IContentSanitizer
{
    private readonly HtmlSanitizer _sanitizer = new();

    public string Sanitize(string rawContent) => _sanitizer.Sanitize(rawContent);
}
