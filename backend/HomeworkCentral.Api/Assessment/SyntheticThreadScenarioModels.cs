namespace HomeworkCentral.Api.Assessment;

public sealed record SyntheticThreadScenario(
    string Category,
    string Requirement,
    string InitialContext,
    IReadOnlyList<SyntheticThreadMessage> Messages,
    /// <summary>LLM-1 self-check verdict from the same generation call (LGTM or REVISE).</summary>
    string? SelfCritiqueVerdict = null,
    /// <summary>Objection the next LLM-1 prompt should resolve when the verdict is REVISE.</summary>
    string? SelfCritiqueFeedback = null);

public sealed record SyntheticThreadMessage(
    int MessageIndex,
    string AuthorId,
    string AuthorRole,
    string Channel,
    string Content,
    bool IsDistractor,
    float ChannelRelevance,
    SyntheticCommunityIntent CommunityIntent,
    float? TeacherEvidence = null,
    float? TeacherRelevance = null,
    float? TeacherApprovalEstimate = null,
    float? TeacherConfidence = null);
