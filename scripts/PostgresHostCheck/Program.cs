using Npgsql;

if (args.Length < 1 || !int.TryParse(args[0], out int port) || port <= 0)
{
    Console.Error.WriteLine("Usage: PostgresHostCheck <port>");
    return 2;
}

string connectionString =
    $"Host=localhost;Port={port};Database=homework_central;Username=postgres;Password=postgres;Timeout=5";

try
{
    await using NpgsqlConnection connection = new(connectionString);
    await connection.OpenAsync();
    await using NpgsqlCommand command = new("SELECT 1", connection);
    object? result = await command.ExecuteScalarAsync();
    return result?.ToString() == "1" ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
