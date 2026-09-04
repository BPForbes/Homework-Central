using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// Projecting a teacher's name-keyed distribution onto the taxonomy axis. The failure that matters
/// here is silent: IndexOf resolves anything it does not recognise to the general bucket, so a
/// hallucinated slug would donate its weight to a real category unless the projection is strict.
/// </summary>
public class ChatMonitoringCategoryDistributionTests
{
    [Fact]
    public void Named_weights_land_on_their_own_categories()
    {
        float[]? distribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation,
            new Dictionary<string, double> { ["harassment"] = 0.7, ["moderation-general"] = 0.3 });

        Assert.NotNull(distribution);
        Assert.Equal(ChatMonitoringCategoryTaxonomy.Moderation.Length, distribution!.Length);
        Assert.Equal(0.7f, distribution[Index("harassment")], precision: 5);
        Assert.Equal(0.3f, distribution[Index("moderation-general")], precision: 5);
    }

    [Fact]
    public void An_unrecognised_slug_is_dropped_rather_than_donated_to_general()
    {
        int general = Index("moderation-general");

        float[]? distribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation,
            new Dictionary<string, double> { ["harassment"] = 0.6, ["not-a-real-category"] = 0.4 });

        Assert.NotNull(distribution);
        Assert.Equal(0.6f, distribution![Index("harassment")], precision: 5);
        // The invented slug must contribute nothing anywhere, least of all to a real label.
        Assert.Equal(0f, distribution[general]);
        Assert.Equal(0.6f, distribution.Sum(), precision: 5);
    }

    [Fact]
    public void TryIndexOf_reports_failure_where_IndexOf_falls_back()
    {
        Assert.False(ChatMonitoringCategoryTaxonomy.TryIndexOf(
            NeuralModelKindChatMonitoring.Moderation, "not-a-real-category", out int _));

        // The lenient lookup this exists to avoid: it answers with the general bucket instead.
        int lenient = ChatMonitoringCategoryTaxonomy.IndexOf(
            NeuralModelKindChatMonitoring.Moderation, "not-a-real-category");
        Assert.InRange(lenient, 0, ChatMonitoringCategoryTaxonomy.Moderation.Length - 1);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Unusable_weights_are_dropped(double weight)
    {
        float[]? distribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation,
            new Dictionary<string, double> { ["harassment"] = weight });

        Assert.Null(distribution);
    }

    [Fact]
    public void An_empty_or_absent_map_yields_no_distribution()
    {
        Assert.Null(ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation, null));
        Assert.Null(ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation, new Dictionary<string, double>()));
    }

    [Fact]
    public void Aliases_of_one_category_are_summed_not_overwritten()
    {
        // NormalizeCategory folds legacy spellings onto current slugs, so two keys can resolve to
        // the same index; the second must not silently replace the first.
        string canonical = "harassment";
        float[]? distribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Moderation,
            new Dictionary<string, double> { [canonical] = 0.4, [canonical.ToUpperInvariant()] = 0.5 });

        Assert.NotNull(distribution);
        Assert.Equal(0.9f, distribution![Index(canonical)], precision: 5);
    }

    [Fact]
    public void Tutoring_uses_its_own_axis()
    {
        float[]? distribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            NeuralModelKindChatMonitoring.Tutoring,
            new Dictionary<string, double> { ["tutoring-mathematics"] = 1.0 });

        Assert.NotNull(distribution);
        Assert.Equal(ChatMonitoringCategoryTaxonomy.Tutoring.Length, distribution!.Length);
        Assert.Equal(
            1f,
            distribution[ChatMonitoringCategoryTaxonomy.IndexOf(
                NeuralModelKindChatMonitoring.Tutoring, "tutoring-mathematics")],
            precision: 5);
    }

    [Fact]
    public void The_generator_parses_category_weights_off_a_message()
    {
        const string json = """
        {
          "category": "harassment",
          "requirement": "Monitor for harassment.",
          "initialContext": "Thread context.",
          "messages": [
            {
              "authorId": "u1",
              "authorRole": "student",
              "channel": "general",
              "content": "You are worthless.",
              "isDistractor": false,
              "channelRelevance": 0.9,
              "categoryWeights": { "harassment": 0.7, "moderation-general": 0.3 }
            }
          ]
        }
        """;

        SyntheticThreadScenario? scenario = SyntheticThreadScenarioGenerator.ParseScenario(json);

        Assert.NotNull(scenario);
        IReadOnlyDictionary<string, double>? weights = scenario!.Messages[0].TeacherCategoryWeights;
        Assert.NotNull(weights);
        Assert.Equal(0.7, weights!["harassment"], precision: 5);
        Assert.Equal(0.3, weights["moderation-general"], precision: 5);
    }

    [Fact]
    public void A_message_without_category_weights_parses_to_null()
    {
        const string json = """
        {
          "category": "harassment",
          "requirement": "Monitor for harassment.",
          "initialContext": "Thread context.",
          "messages": [
            { "authorId": "u1", "authorRole": "student", "channel": "general",
              "content": "You are worthless.", "isDistractor": false, "channelRelevance": 0.9 }
          ]
        }
        """;

        SyntheticThreadScenario? scenario = SyntheticThreadScenarioGenerator.ParseScenario(json);

        Assert.NotNull(scenario);
        Assert.Null(scenario!.Messages[0].TeacherCategoryWeights);
    }

    private static int Index(string slug) =>
        ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, slug);
}
