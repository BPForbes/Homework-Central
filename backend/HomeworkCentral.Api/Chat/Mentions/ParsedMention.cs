namespace HomeworkCentral.Api.Chat.Mentions;

/// <summary>A single @-token discovered in message content.</summary>
public sealed record ParsedMention(
    MentionKind Kind,
    string Token,
    int StartIndex,
    int Length,
    bool IsActive);
