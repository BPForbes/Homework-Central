namespace HomeworkCentral.Api.Chat;

/// <summary>
/// Builds the short, single-line preview of a parent message's content that's denormalized onto
/// a reply (<see cref="Models.ChatMessage.ReplyToContentSnippet"/>) so the quoted preview can
/// render without a join back to the original row.
/// </summary>
public static class ChatReplySnippet
{
    public const int MaxLength = 160;

    public static string Build(string content, int maxLength = MaxLength)
    {
        string collapsed = string.Join(' ', content.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= maxLength)
            return collapsed;

        return string.Concat(collapsed.AsSpan(0, maxLength).ToString().TrimEnd(), "…");
    }
}
