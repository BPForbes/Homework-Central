namespace HomeworkCentral.Api.Uploads;

public class UploadOptions
{
    public string RootPath { get; set; } = "App_Data/uploads";
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
    public int OrphanTtlHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 60;
}
