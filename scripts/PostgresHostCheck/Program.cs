using System.Net.Sockets;
using Npgsql;

if (args.Length is < 1 or > 2
    || !int.TryParse(args[0], out int port)
    || port is <= 0 or > 65535)
{
    Console.Error.WriteLine("Usage: PostgresHostCheck <port> [host]");
    return 2;
}

// 127.0.0.1 — not localhost — so Windows does not spend the connect timeout on ::1
// while Docker Desktop has published IPv4 only.
string host = args.Length == 2 && !string.IsNullOrWhiteSpace(args[1])
    ? args[1].Trim()
    : "127.0.0.1";

if (!IsSafeHost(host))
{
    Console.Error.WriteLine("Host must be a hostname or IP address.");
    return 2;
}

string connectionString =
    $"Host={host};Port={port};Database=homework_central_master;Username=postgres;Password=postgres;Timeout=5";

try
{
    await using NpgsqlConnection connection = new(connectionString);
    await connection.OpenAsync();
    await using NpgsqlCommand command = new("SELECT 1", connection);
    object? result = await command.ExecuteScalarAsync();
    return result?.ToString() == "1" ? 0 : 1;
}
catch (NpgsqlException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (SocketException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static bool IsSafeHost(string host)
{
    return host.Length > 0
        && host.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '[' or ']');
}
