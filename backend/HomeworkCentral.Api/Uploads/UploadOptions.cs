namespace HomeworkCentral.Api.Uploads;

public class UploadOptions
{
    public string RootPath { get; set; } = "App_Data/uploads";
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
    public string[] AllowedContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "application/pdf",
        "text/plain",
    ];
}
