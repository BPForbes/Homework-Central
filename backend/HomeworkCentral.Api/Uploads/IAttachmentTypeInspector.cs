namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// Sniffs attachment bytes to classify content type. Browser-supplied content types are hints only.
/// </summary>
public interface IAttachmentTypeInspector
{
    /// <summary>
    /// Inspects <paramref name="stream"/> from the current position without consuming ownership of the stream.
    /// </summary>
    AttachmentTypeInspectionResult Inspect(Stream stream, string? browserContentType);
}
