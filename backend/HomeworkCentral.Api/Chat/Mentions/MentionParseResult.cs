namespace HomeworkCentral.Api.Chat.Mentions;

/// <summary>Output of parsing and normalizing mention tokens in a chat message.</summary>
public sealed record MentionParseResult(
    string DisplayContent,
    IReadOnlyList<ParsedMention> ActiveMentions,
    bool ContainsAnyMentionToken);
