using HomeworkCentral.Api.Dev;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HomeworkCentral.Api.Tests.Dev;

public class DevStartupWarmupTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void ShouldSkip_requires_explicit_development_flag(string? flag, bool expected)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevStartupWarmup.SkipEnvVarName] = flag })
            .Build();

        Assert.Equal(expected, DevStartupWarmup.ShouldSkip(config, new TestHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void ShouldSkip_is_disabled_outside_development()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevStartupWarmup.SkipEnvVarName] = "1" })
            .Build();

        Assert.False(DevStartupWarmup.ShouldSkip(config, new TestHostEnvironment(Environments.Production)));
    }

    private sealed class TestHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HomeworkCentral.Api.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
