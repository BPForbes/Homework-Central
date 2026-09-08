using System.Net.Sockets;
using HomeworkCentral.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Data;

public class DatabaseStartupTests
{
    [Fact]
    public void IsTransient_is_true_for_npgsql_connect_failure()
    {
        NpgsqlException ex = new(
            "Failed to connect to 127.0.0.1:5434",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(DatabaseStartup.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_is_true_for_timeout()
    {
        Assert.True(DatabaseStartup.IsTransient(new TimeoutException("Postgres warmup")));
    }

    [Fact]
    public void IsTransient_is_false_for_non_operational_failure()
    {
        Assert.False(DatabaseStartup.IsTransient(new InvalidOperationException("bad migration")));
    }

    [Fact]
    public void IsHostUnreachable_is_true_for_socket_refused()
    {
        SocketException ex = new((int)SocketError.ConnectionRefused);

        Assert.True(DatabaseStartup.IsHostUnreachable(ex));
    }

    [Fact]
    public void IsHostUnreachable_is_true_for_npgsql_failed_to_connect()
    {
        NpgsqlException ex = new("Failed to connect to 127.0.0.1:5434");

        Assert.True(DatabaseStartup.IsHostUnreachable(ex));
    }

    [Fact]
    public void IsHostUnreachable_walks_inner_exceptions()
    {
        InvalidOperationException ex = new(
            "migrate",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(DatabaseStartup.IsHostUnreachable(ex));
    }

    [Fact]
    public void IsHostUnreachable_is_false_for_timeout_without_socket()
    {
        Assert.False(DatabaseStartup.IsHostUnreachable(new TimeoutException("query")));
    }

    [Fact]
    public void IsTransient_is_true_for_socket_refused()
    {
        Assert.True(DatabaseStartup.IsTransient(new SocketException((int)SocketError.ConnectionRefused)));
    }

    [Fact]
    public void ShouldRetryDevelopment_is_unbounded_only_for_unreachable_host()
    {
        NpgsqlException unreachable = new("Failed to connect to 127.0.0.1:5434");
        NpgsqlException otherTransient = new("too many clients already");

        Assert.True(DatabaseStartup.ShouldRetryDevelopment(unreachable, DatabaseStartup.MaxAttempts));
        Assert.True(DatabaseStartup.ShouldRetryDevelopment(unreachable, DatabaseStartup.MaxAttempts + 5));
        Assert.True(DatabaseStartup.ShouldRetryDevelopment(otherTransient, DatabaseStartup.MaxAttempts - 1));
        Assert.False(DatabaseStartup.ShouldRetryDevelopment(otherTransient, DatabaseStartup.MaxAttempts));
        Assert.False(DatabaseStartup.ShouldRetryDevelopment(new InvalidOperationException("bad migration"), 1));
    }

    [Fact]
    public async Task InitializeDevelopmentAsync_throws_when_already_cancelled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DatabaseStartup.InitializeDevelopmentAsync(new EmptyServiceProvider(), cts.Token));
    }

    [Fact]
    public async Task InitializeDevelopmentAsync_stops_after_max_attempts_for_other_transient_failures()
    {
        TimeSpan previousDelay = DatabaseStartup.RetryDelay;
        DatabaseStartup.RetryDelay = TimeSpan.Zero;
        ThrowingScopeProvider services = new(new NpgsqlException("too many clients already"));
        try
        {
            await Assert.ThrowsAsync<NpgsqlException>(() =>
                DatabaseStartup.InitializeDevelopmentAsync(services, CancellationToken.None));
            Assert.Equal(DatabaseStartup.MaxAttempts, services.AttemptCount);
        }
        finally
        {
            DatabaseStartup.RetryDelay = previousDelay;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class ThrowingScopeProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly Exception _failure;

        public ThrowingScopeProvider(Exception failure)
        {
            _failure = failure;
        }

        public int AttemptCount { get; private set; }

        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
                return this;

            if (serviceType == typeof(ILogger<Program>))
                return null;

            AttemptCount++;
            throw _failure;
        }
    }
}
