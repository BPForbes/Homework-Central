namespace HomeworkCentral.Api.Assessment;

public sealed record SyntheticThreadScenario(
    string Category,
    string Requirement,
    string InitialContext,
    IReadOnlyList<SyntheticThreadMessage> Messages);

public sealed record SyntheticThreadMessage(
    int MessageIndex,
    string AuthorId,
    string AuthorRole,
    string Channel,
    string Content,
    bool IsDistractor,
    float ChannelRelevance,
    SyntheticCommunityIntent CommunityIntent);