namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// MIME/hazard classification from byte sniffing. Hazards never receive an
/// <see cref="InlinePreviewKind"/>; non-hazard kinds are <c>image</c>, <c>video</c>,
/// <c>audio</c>, <c>pdf</c>, or <c>text</c>.
/// </summary>
public sealed record AttachmentTypeInspectionResult(
    string ContentType,
    bool IsHazard,
    bool SupportsInlinePreview,
    string? InlinePreviewKind);
