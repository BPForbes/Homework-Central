namespace HomeworkCentral.Api.Dev;

/// <summary>
/// Local stripped run-dev mode. Development-only: pause leftover training rows and skip
/// neural warmup/refresh so the API does not resume hashed-MLP or Ollama work on boot.
/// </summary>
public static class DevNeuralEnvironmentPause
{
    public const string EnvVarName = "HC_DEV_STRIPPED";

    /// <summary>
    /// Returns true only for an explicit development-mode opt-in. Production never pauses
    /// neural environments from this switch.
    /// </summary>
    public static bool ShouldPause(IConfiguration config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            return false;

        string? flag = config[EnvVarName] ?? Environment.GetEnvironmentVariable(EnvVarName);
        return string.Equals(flag, "1", StringComparison.Ordinal);
    }
}
