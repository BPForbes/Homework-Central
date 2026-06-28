using System.Diagnostics;
using System.Runtime.InteropServices;

static string FindRepoRoot()
{
    DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException(
        "Could not find repository root (expected docker-compose.yml in an ancestor directory).");
}

static int RunScript(string repoRoot)
{
    bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    string scriptName = isWindows ? "run-dev.ps1" : "run-dev.sh";
    string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

    if (!File.Exists(scriptPath))
    {
        Console.Error.WriteLine($"error: dev script not found: {scriptPath}");
        return 1;
    }

    ProcessStartInfo startInfo;
    if (isWindows)
    {
        string shell = OperatingSystem.IsWindows() && File.Exists(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")
            : "powershell.exe";

        startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
    }
    else
    {
        startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"\"{scriptPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
    }

    // IDE already compiled the API via the DevHost project reference; still install frontend deps.
    startInfo.Environment["HC_SKIP_DOTNET_BUILD"] = "1";

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dev stack script.");

    process.WaitForExit();
    return process.ExitCode;
}

try
{
    string repoRoot = FindRepoRoot();
    Environment.CurrentDirectory = repoRoot;
    return RunScript(repoRoot);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
