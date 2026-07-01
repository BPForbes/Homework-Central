using HomeworkCentral.Api.Security;

namespace HomeworkCentral.Api.Tests.Security;

public class HtmlContentSanitizerTests
{
    private readonly HtmlContentSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_strips_script_tags()
    {
        string result = _sanitizer.Sanitize("<p>Hello</p><script>alert(1)</script>");

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", result);
    }

    [Fact]
    public void Sanitize_strips_event_handlers()
    {
        string result = _sanitizer.Sanitize("<img src=\"x\" onerror=\"alert(1)\" />");

        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_strips_javascript_urls()
    {
        string result = _sanitizer.Sanitize("<a href=\"javascript:alert(1)\">link</a>");

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }
}
