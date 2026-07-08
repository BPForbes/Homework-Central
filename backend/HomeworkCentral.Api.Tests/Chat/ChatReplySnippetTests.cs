using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatReplySnippetTests
{
    [Fact]
    public void Short_content_is_returned_unchanged()
    {
        Assert.Equal("Hello there", ChatReplySnippet.Build("Hello there"));
    }

    [Fact]
    public void Newlines_and_repeated_whitespace_are_collapsed_to_single_spaces()
    {
        Assert.Equal("Line one Line two", ChatReplySnippet.Build("Line one\n\n  Line   two"));
    }

    [Fact]
    public void Long_content_is_truncated_with_an_ellipsis()
    {
        string longContent = new string('a', 300);

        string snippet = ChatReplySnippet.Build(longContent, maxLength: 160);

        Assert.Equal(161, snippet.Length);
        Assert.EndsWith("…", snippet);
        Assert.StartsWith(new string('a', 160), snippet);
    }

    [Fact]
    public void Truncation_boundary_does_not_leave_trailing_whitespace_before_the_ellipsis()
    {
        // The cut point (index 159, the 160th character) lands exactly on the single space
        // separating the two runs, so TrimEnd must remove it before the ellipsis is appended.
        string content = string.Concat(new string('a', 159), " ", new string('b', 50));

        string snippet = ChatReplySnippet.Build(content, maxLength: 160);

        Assert.False(snippet.Contains(" …", StringComparison.Ordinal));
        Assert.Equal(new string('a', 159) + "…", snippet);
    }
}
