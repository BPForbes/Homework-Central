namespace HomeworkCentral.Api.Uploads;

public sealed record AttachmentTypeInspectionResult(
    string ContentType,
    bool IsHazard,
    bool SupportsInlinePreview,
    string? InlinePreviewKind);
