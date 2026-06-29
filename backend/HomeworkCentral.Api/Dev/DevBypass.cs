namespace HomeworkCentral.Api.Dev;

/// <summary>
/// Guards the localhost-only developer bypass. Enabled when HC_DEV_BYPASS is set by
/// dev startup scripts and ASPNETCORE_ENVIRONMENT is Development.
/// </summary>
public static class DevBypass
{
    public const string EnvVarName = "HC_DEV_BYPASS";
    public const string DevAdminUsername = "DevAdmin";
    public const string DevAdminEmail = "devadmin@localhost.local";

    /// <summary>Returns true when dev bypass is active for the current host process.</summary>
    public static bool IsEnabled(IConfiguration config, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment())
            return false;

        string? flag = config[EnvVarName] ?? Environment.GetEnvironmentVariable(EnvVarName);
        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Restricts dev bypass HTTP endpoints to loopback callers only.</summary>
    public static bool IsLocalhost(HttpContext ctx)
    {
        ConnectionInfo connection = ctx.Connection;
        if (connection.RemoteIpAddress is null)
            return true;

        if (System.Net.IPAddress.IsLoopback(connection.RemoteIpAddress))
            return true;

        string? host = ctx.Request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal);
    }
}
