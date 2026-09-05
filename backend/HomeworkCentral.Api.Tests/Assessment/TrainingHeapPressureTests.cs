using HomeworkCentral.Api.Assessment;
using Xunit;

namespace HomeworkCentral.Api.Tests.Assessment;

public sealed class TrainingHeapPressureTests
{
    [Theory]
    [InlineData(70, 70, 1, 100, true)]
    [InlineData(69, 70, 1, 100, false)]
    [InlineData(10, 0, 70, 100, true)]
    [InlineData(10, 0, 69, 100, false)]
    [InlineData(-1, 10, 0, 0, false)]
    [InlineData(0, 0, 0, 0, false)]
    public void DecideSpill_matches_rust_watermark_rule(
        long used,
        long watermark,
        long rss,
        long limit,
        bool expected)
    {
        Assert.Equal(expected, TrainingHeapPressure.DecideSpill(used, watermark, rss, limit));
    }

    [Fact]
    public void HighWatermarkBytes_is_seventy_percent_of_limit()
    {
        Assert.Equal(70, TrainingHeapPressure.HighWatermarkBytes(100));
        Assert.Equal(0, TrainingHeapPressure.HighWatermarkBytes(0));
    }

    [Fact]
    public void ShouldSkipTraces_is_true_when_heap_is_already_at_spill()
    {
        TrainingHeapSample sample = new(70, 70, 1, 100);
        Assert.True(TrainingHeapPressure.ShouldSpill(sample));
        Assert.True(TrainingHeapPressure.ShouldSkipTraces(sample));
    }

    [Fact]
    public void ShouldSkipTraces_is_true_before_spill_watermark()
    {
        TrainingHeapSample sample = new(56, 70, 1, 100);
        Assert.False(TrainingHeapPressure.ShouldSpill(sample));
        Assert.True(TrainingHeapPressure.ShouldSkipTraces(sample));
    }
}

public sealed class TrainingSpillCheckpointTests
{
    [Fact]
    public void RoundTrip_preserves_parameter_snapshot()
    {
        NeuralNetParameterSnapshot parameters = new(
            3,
            4,
            "ieee754-float32-le",
            "dense-base64",
            2,
            "AAAAAA==",
            "abc");
        string json = TrainingSpillCheckpoint.Serialize(
            Guid.Parse("d9908cb9-f58b-44cf-a41f-82bc9ec9240f"),
            NeuralModelKindChatMonitoring.Moderation,
            169,
            parameters);

        Assert.True(TrainingSpillCheckpoint.TryParse(json, out TrainingSpillCheckpoint? checkpoint));
        Assert.NotNull(checkpoint);
        Assert.Equal(TrainingSpillCheckpoint.Version, checkpoint.SchemaVersion);
        Assert.Equal(169, checkpoint.TicketsProcessed);
        Assert.Equal(parameters.PackedValues, checkpoint.Parameters.PackedValues);
        Assert.Equal(parameters.Checksum, checkpoint.Parameters.Checksum);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ \"schemaVersion\": \"replay-v2\" }")]
    public void TryParse_rejects_non_spill_payloads(string? json)
    {
        Assert.False(TrainingSpillCheckpoint.TryParse(json, out TrainingSpillCheckpoint? checkpoint));
        Assert.Null(checkpoint);
    }
}

public sealed class NeuralMeshTopKTests
{
    [Fact]
    public void SelectTopKManaged_keeps_largest_absolute_values()
    {
        float[] values = [0.1f, -2f, 0f, 3f, 1e-8f];
        int[] indexes = [10, 11, 12, 13, 14];

        List<int> selected = NeuralMeshFrameExtractor.SelectTopKManaged(values, indexes, take: 2, distinct: false);

        Assert.Equal([13, 11], selected);
    }
}
