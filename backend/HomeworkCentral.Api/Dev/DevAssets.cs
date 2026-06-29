namespace HomeworkCentral.Api.Dev;

/// <summary>
/// Dev-only static asset locations. Favicon.svg has a single canonical copy in the
/// frontend public folder; the API project links that file into its build output.
/// </summary>
public static class DevAssets
{
    /// <summary>Repository-relative path to the canonical favicon.svg.</summary>
    public const string CanonicalFaviconRepoPath = "frontend/public/favicon.svg";

    /// <summary>Subdirectory under the API build output where linked dev assets are copied.</summary>
    public const string OutputSubdirectory = "Dev";

    /// <summary>Returns the absolute path to favicon.svg in the API build output directory.</summary>
    public static string FaviconOutputPath(string applicationBaseDirectory) =>
        Path.Combine(applicationBaseDirectory, OutputSubdirectory, "favicon.svg");
}
