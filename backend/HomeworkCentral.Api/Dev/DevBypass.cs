namespace HomeworkCentral.Api.Dev;

public static class DevBypass
{
    public const string EnvVarName = "HC_DEV_BYPASS";
    public const string DevAdminUsername = "DevAdmin";
    public const string DevAdminEmail = "devadmin@localhost.local";

    public static bool IsEnabled(IConfiguration config, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment())
            return false;

        string? flag = config[EnvVarName] ?? Environment.GetEnvironmentVariable(EnvVarName);
        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

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
