using HomeworkCentral.Api.Dev;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HomeworkCentral.Api.Tests.Dev;

public class DevNeuralEnvironmentPauseTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void ShouldPause_requires_explicit_development_flag(string? flag, bool expected)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevNeuralEnvironmentPause.EnvVarName] = flag })
            .Build();

        Assert.Equal(expected, DevNeuralEnvironmentPause.ShouldPause(config, new TestHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void ShouldPause_is_disabled_outside_development()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DevNeuralEnvironmentPause.EnvVarName] = "1" })
            .Build();

        Assert.False(DevNeuralEnvironmentPause.ShouldPause(config, new TestHostEnvironment(Environments.Production)));
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
