namespace HomeworkCentral.Api.Dev;

/// <summary>Controls optional development-only startup work for a known-warm local database.</summary>
public static class DevStartupWarmup
{
    public const string SkipEnvVarName = "HC_SKIP_DEV_WARMUP";

    /// <summary>
    /// Returns true only for an explicit development-mode opt-out. Production always performs
    /// its normal startup path and this switch is never enabled by the development scripts.
    /// </summary>
    public static bool ShouldSkip(IConfiguration config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            return false;

        string? flag = config[SkipEnvVarName] ?? Environment.GetEnvironmentVariable(SkipEnvVarName);
        return string.Equals(flag, "1", StringComparison.Ordinal);
    }
}
