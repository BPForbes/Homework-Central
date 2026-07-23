using HomeworkCentral.Api.Assessment;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class SyntheticConceptCoverageSamplerTests
{
    [Fact]
    public void NextTarget_Moderation_CoversAllFilterableConceptsBeforeRepeating()
    {
        SyntheticConceptCoverageSampler sampler = new(seed: 42);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int taxonomySize = ChatMonitoringCategoryTaxonomy.Moderation.Length;

        for (int ticketIndex = 1; ticketIndex <= taxonomySize; ticketIndex++)
        {
            string slug = sampler.NextTarget(NeuralTrainingMode.Moderation, ticketIndex);
            Assert.Contains(ChatMonitoringCategoryTaxonomy.Moderation, label =>
                string.Equals(label, slug, StringComparison.OrdinalIgnoreCase));
            Assert.True(seen.Add(slug), $"Repeated {slug} before full taxonomy coverage.");
        }

        Assert.Equal(taxonomySize, seen.Count);
        Assert.All(
            ChatMonitoringCategoryTaxonomy.Moderation,
            slug => Assert.Equal(1, sampler.Counts.GetValueOrDefault(slug)));
    }

    [Fact]
    public void NextTarget_DoesNotCollapseOntoPaymentSolicitation()
    {
        SyntheticConceptCoverageSampler sampler = new(seed: 7);
        List<string> firstTwenty = Enumerable.Range(1, 20)
            .Select(index => sampler.NextTarget(NeuralTrainingMode.Moderation, index))
            .ToList();

        Assert.Contains(firstTwenty, slug =>
            !string.Equals(slug, "payment-solicitation", StringComparison.OrdinalIgnoreCase));
        Assert.True(firstTwenty.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 18);
    }

    [Fact]
    public void NextTarget_Both_AlternatesLineages()
    {
        SyntheticConceptCoverageSampler sampler = new(seed: 11);
        string odd = sampler.NextTarget(NeuralTrainingMode.Both, ticketIndex: 1);
        string even = sampler.NextTarget(NeuralTrainingMode.Both, ticketIndex: 2);

        Assert.Contains(ChatMonitoringCategoryTaxonomy.Moderation, label =>
            string.Equals(label, odd, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ChatMonitoringCategoryTaxonomy.Tutoring, label =>
            string.Equals(label, even, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Underrepresented_ReturnsLowestCountLabels()
    {
        SyntheticConceptCoverageSampler sampler = new(seed: 3);
        sampler.Record("payment-solicitation");
        sampler.Record("payment-solicitation");
        IReadOnlyList<string> gaps = sampler.Underrepresented(NeuralModelKindChatMonitoring.Moderation, take: 5);

        Assert.Equal(5, gaps.Count);
        Assert.DoesNotContain("payment-solicitation", gaps);
        Assert.All(gaps, slug => Assert.Equal(0, sampler.Counts.GetValueOrDefault(slug)));
    }
}
