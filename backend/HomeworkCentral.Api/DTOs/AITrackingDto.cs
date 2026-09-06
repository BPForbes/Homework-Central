namespace HomeworkCentral.Api.DTOs;

public sealed class AIModelLineageDto
{
    public int LineageId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public Guid? PortalChannelId { get; set; }
    public int CategoryCount { get; set; }
    public int SessionCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AICategoryDto
{
    public int CategoryId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCatchAll { get; set; }
}

public sealed class RegisterAIModelLineageRequest
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid? PortalChannelId { get; set; }
    public List<RegisterAICategoryRequest> Categories { get; set; } = [];
}

public sealed class RegisterAICategoryRequest
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsCatchAll { get; set; }
}

public sealed class AITrackingCategoryWeightDto
{
    public long CategoryWeightId { get; set; }
    public string CategorySlug { get; set; } = string.Empty;
    public double Weight { get; set; }
    public bool IsHumanCorrected { get; set; }
    public string? HumanOverrideCategorySlug { get; set; }
    public DateTime? HumanCorrectionAtUtc { get; set; }
}

public sealed class AITrackingPredictionDto
{
    public long PredictionId { get; set; }
    public string PredictedCategorySlug { get; set; } = string.Empty;
    public float PredictedScore { get; set; }
    public string? ActualCategorySlug { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AITrackingSessionDto
{
    public long SessionId { get; set; }
    public string LineageSlug { get; set; } = string.Empty;
    public Guid? TicketId { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public int MessageIndex { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public IReadOnlyList<AITrackingCategoryWeightDto> CategoryWeights { get; set; } = [];
    public IReadOnlyList<AITrackingPredictionDto> Predictions { get; set; } = [];
}

public sealed class AITrackingDeleteResultDto
{
    public int DeletedSessionCount { get; set; }
}
