namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// clamd settings (config <c>ClamAv</c>). Disabled, timeout, or unreachable scans store
/// <see cref="MalwareScanResult.NotScanned"/> so download fails open rather than quarantining.
/// </summary>
public class ClamAvOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 120;
}
