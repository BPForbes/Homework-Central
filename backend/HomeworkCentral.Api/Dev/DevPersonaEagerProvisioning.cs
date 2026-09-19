namespace HomeworkCentral.Api.Dev;

/// <summary>
/// Controls whether the dev persona databases are all pre-provisioned in the background at
/// startup. Off by default: provisioning ~70 tenant databases hammers the small local Postgres
/// container for minutes, and personas are provisioned on demand at dev login anyway (see
/// AuthService.DevLoginAsync), so only the personas actually in use — one per logged-in
/// tab/session — pay the cost.
/// </summary>
public static class DevPersonaEagerProvisioning
{
    public const string EnvVarName = "HC_DEV_PROVISION_ALL_PERSONAS";

    public static bool IsEnabled(IConfiguration config)
    {
        string? flag = config[EnvVarName] ?? Environment.GetEnvironmentVariable(EnvVarName);
        return string.Equals(flag, "1", StringComparison.Ordinal);
    }
}
