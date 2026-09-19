namespace HomeworkCentral.Api.Assessment;

public sealed record SyntheticThreadScenario(
    string Category,
    string Requirement,
    string InitialContext,
    IReadOnlyList<SyntheticThreadMessage> Messages,
    /// <summary>Training-LLM self-check verdict from the same generation call (LGTM or REVISE).</summary>
    string? SelfCritiqueVerdict = null,
    /// <summary>Objection the next training-LLM prompt should resolve when the verdict is REVISE.</summary>
    string? SelfCritiqueFeedback = null);

/// <summary>Normalized synthetic ticket used by neural-net training and the multipurpose training LLM.</summary>
public sealed record SyntheticTicket(
    string Category,
    string Requirement,
    string Message,
    string ContextSnapshot,
    double ExpectedScore,
    double ExpectedRelevance,
    IReadOnlyList<SyntheticThreadMessage> Messages,
    string? SelfCritiqueVerdict = null,
    string? SelfCritiqueFeedback = null);

/// <summary>Teacher / self-critique labels produced by the training LLM (never a second model).</summary>
public sealed record SyntheticEvaluatorResult(
    string Verdict,
    double TargetScore,
    double TargetRelevance,
    string Feedback,
    double ApprovalEstimate,
    double EvaluatorConfidence);

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
    float? TeacherConfidence = null,
    /// <summary>
    /// Sparse category-name to weight map from the training LLM — the soft label for this message.
    /// Naming a few categories is a request a model can satisfy reliably, unlike emitting a
    /// hundred-element array in taxonomy order. Null means the teacher offered no distribution and
    /// the hard category is used instead.
    /// </summary>
    IReadOnlyDictionary<string, double>? TeacherCategoryWeights = null);
