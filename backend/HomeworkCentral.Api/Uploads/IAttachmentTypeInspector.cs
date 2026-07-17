namespace HomeworkCentral.Api.Uploads;

public interface IAttachmentTypeInspector
{
    AttachmentTypeInspectionResult Inspect(Stream stream, string? browserContentType);
}
