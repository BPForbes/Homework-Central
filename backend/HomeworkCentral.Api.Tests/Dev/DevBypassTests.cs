using HomeworkCentral.Api.Dev;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HomeworkCentral.Api.Tests.Dev;

public class DevBypassTests
{
    [Fact]
    public void IsEnabled_requires_development_environment()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevBypass.EnvVarName] = "1" })
            .Build();

        Assert.False(DevBypass.IsEnabled(config, new TestWebHostEnvironment(Environments.Production)));
        Assert.True(DevBypass.IsEnabled(config, new TestWebHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void IsLocalhost_accepts_only_loopback_remote_addresses()
    {
        DefaultHttpContext loopback = new() { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback } };
        DefaultHttpContext remote = new() { Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10") } };
        DefaultHttpContext missingRemoteIp = new();

        Assert.True(DevBypass.IsLocalhost(loopback));
        Assert.False(DevBypass.IsLocalhost(remote));
        Assert.True(DevBypass.IsLocalhost(missingRemoteIp));
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HomeworkCentral.Api.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
