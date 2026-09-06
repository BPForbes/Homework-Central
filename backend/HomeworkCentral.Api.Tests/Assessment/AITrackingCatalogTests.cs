using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public class AITrackingCatalogTests
{
    [Fact]
    public void Built_in_kinds_map_to_stable_slugs()
    {
        Assert.Equal("moderation", AITrackingCatalog.SlugFor(NeuralModelKindChatMonitoring.Moderation));
        Assert.Equal("tutoring", AITrackingCatalog.SlugFor(NeuralModelKindChatMonitoring.Tutoring));
    }

    [Theory]
    [InlineData("moderation", NeuralModelKindChatMonitoring.Moderation)]
    [InlineData("Tutoring", NeuralModelKindChatMonitoring.Tutoring)]
    public void Built_in_slugs_round_trip(string slug, NeuralModelKindChatMonitoring expected)
    {
        Assert.True(AITrackingCatalog.TryParseBuiltInKind(slug, out NeuralModelKindChatMonitoring kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("hr-intake")]
    [InlineData("")]
    [InlineData(null)]
    public void Custom_or_empty_slugs_are_not_built_in(string? slug)
    {
        Assert.False(AITrackingCatalog.TryParseBuiltInKind(slug, out _));
    }
}
