namespace HomeworkCentral.Api.Uploads;

/// <summary>
/// ClamAV / clamd connection settings. When <see cref="Enabled"/> is false, the scanner
/// returns <see cref="MalwareScanResult.NotScanned"/> (fail-open for download). Bound from
/// configuration section <c>ClamAv</c>.
/// </summary>
public class ClamAvOptions
{
    /// <summary>When false, skip scanning and store <see cref="MalwareScanResult.NotScanned"/>.</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;

    /// <summary>Per-scan timeout in seconds; timeouts surface as <see cref="MalwareScanResult.NotScanned"/>.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
