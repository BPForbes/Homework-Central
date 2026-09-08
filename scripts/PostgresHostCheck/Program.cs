using System.Net.Sockets;
using Npgsql;

// Exit codes are a contract with scripts/dev-stack-lib.* and scripts/run-dev.*:
//   0 — homework_central_master accepted the connection and answered a query.
//   1 — the host cannot reach a Postgres server on this address at all.
//   2 — bad usage.
//   3 — a server answered on this address but did not hand back a usable
//       homework_central_master, for a reason other than the two below. The expected case is
//       a fresh volume that has no such database yet, which is why readiness waits accept
//       this as "the published port reaches Docker Postgres": run-dev creates the database
//       only after the wait returns. This is also the fallback for every SqlState that is
//       not mapped, so it does not on its own prove the database is merely missing.
//   4 — a server answered with 57P03: it is starting up or shutting down and is not taking
//       sessions yet. Readiness waits keep waiting, since that state clears on its own.
//   5 — a server answered with 28P01: it rejected the dev credentials, so the volume was
//       initialised with a different password. Readiness waits accept it for the same
//       reason as 3, and run-dev recreates its own Postgres volume on it.
//
//       Kept separate from 3 because this is the only signal that carries it. initdb
//       writes `host all all 127.0.0.1/32 trust` ahead of the image's scram-sha-256 rule,
//       so a psql probe run inside the container over loopback authenticates against no
//       password at all and cannot tell a mismatched volume from a healthy one.

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

// These credentials must stay equal to DEV_STACK_POSTGRES_PASSWORD in scripts/dev-stack-lib.sh
// and $script:DevPostgresPassword in scripts/dev-stack-lib.ps1, which are what run-dev writes into
// .env and exports to Compose. A divergence would report 28P01 against a healthy volume, and exit 5
// authorises run-dev to destroy that volume. Passing the password as an argument instead would put
// it in the process list on a shared machine.
string connectionString =
    $"Host={host};Port={port};Database=homework_central_master;Username=postgres;Password=postgres;Timeout=5";

try
{
    await using NpgsqlConnection connection = new(connectionString);
    await connection.OpenAsync();
    await using NpgsqlCommand command = new("SELECT 1", connection);
    object? result = await command.ExecuteScalarAsync();
    if (result?.ToString() == "1")
    {
        return 0;
    }

    // The session opened, so the address reaches Postgres; only the query result is
    // unexpected. Report that as "answered but unusable" rather than "unreachable".
    Console.Error.WriteLine($"SELECT 1 returned '{result}'");
    return 3;
}
catch (PostgresException ex)
{
    Console.Error.WriteLine(Describe(ex));
    return ex.SqlState switch
    {
        PostgresErrorCodes.CannotConnectNow => 4,
        PostgresErrorCodes.InvalidPassword => 5,
        _ => 3,
    };
}
catch (NpgsqlException ex)
{
    Console.Error.WriteLine(Describe(ex));
    return 1;
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine(Describe(ex));
    return 1;
}
catch (SocketException ex)
{
    Console.Error.WriteLine(Describe(ex));
    return 1;
}

static bool IsSafeHost(string host)
{
    if (host.Length == 0
        || !host.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '[' or ']'))
    {
        return false;
    }

    // A colon is allowed only for a bracketed IPv6 literal. Npgsql also reads `Host=name:port`,
    // so a bare colon here would let the host argument reach a port that the numeric check on
    // args[0] already rejected.
    return (host.StartsWith('[') && host.EndsWith(']')) || !host.Contains(':');
}

// Npgsql's own message for an unreachable host is only "Failed to connect to <host>:<port>";
// the reason (refused, timed out, no route) lives in the socket exception underneath, and the
// readiness waits print this line when they give up.
static string Describe(Exception exception)
{
    return exception.InnerException is null
        ? exception.Message
        : $"{exception.Message} ({exception.InnerException.Message})";
}
