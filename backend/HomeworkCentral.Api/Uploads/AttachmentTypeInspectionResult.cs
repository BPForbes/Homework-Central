namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Byte-sniffed type. Hazards never get an <see cref="InlinePreviewKind"/> (no inline media/code preview).
/// </summary>
public sealed record AttachmentTypeInspectionResult(
    string ContentType,
    bool IsHazard,
    bool SupportsInlinePreview,
    string? InlinePreviewKind);
