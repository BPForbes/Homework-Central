namespace HomeworkCentral.Api.Data;

internal static class DesignTimeConnection
{
    public static string GetMasterConnection()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__MasterConnection");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        string port = Environment.GetEnvironmentVariable("POSTGRES_HOST_PORT") ?? "5434";
        return $"Host=localhost;Port={port};Database=homework_central_master;Username=postgres;Password=postgres";
    }
}
