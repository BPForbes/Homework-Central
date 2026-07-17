namespace HomeworkCentral.Api.Uploads;

public class ClamAvOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 120;
}
