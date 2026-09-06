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

    [Fact]
    public void ShouldAttemptSpill_waits_for_relief_below_skip_trace()
    {
        TrainingHeapPressure.ResetForTests();
        TrainingHeapPressure.NoteSuccessfulSpill();

        Assert.False(TrainingHeapPressure.ShouldAttemptSpill(new TrainingHeapSample(70, 70, 1, 100)));
        Assert.True(TrainingHeapPressure.ShouldSkipTraces(new TrainingHeapSample(70, 70, 1, 100)));
        Assert.False(TrainingHeapPressure.ShouldAttemptSpill(new TrainingHeapSample(10, 70, 1, 100)));
        Assert.True(TrainingHeapPressure.ShouldAttemptSpill(new TrainingHeapSample(70, 70, 1, 100)));
    }

    [Theory]
    [InlineData("18000000", 18000000)]
    [InlineData("0x112A880", 18000000)]
    [InlineData("112A880", 18000000)]
    public void TryParseHeapHardLimit_accepts_decimal_and_hex(string raw, long expected)
    {
        Assert.True(TrainingHeapPressure.TryParseHeapHardLimit(raw, out long bytes));
        Assert.Equal(expected, bytes);
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
        if (RustKernels.HasTopKAbs)
        {
            Assert.True(RustKernels.TryTopKAbs(values, indexes, 2, out int[] rustSelected));
            Assert.Equal(selected, rustSelected);
        }
    }

    [Fact]
    public void SelectTopKFromSparse_distinct_unique_then_cap_keeps_largest_abs_per_index()
    {
        List<SparseValue> values =
        [
            new(1, 0.4f),
            new(1, 2f),
            new(2, -3f),
            new(3, 1.5f),
            new(2, 0.1f),
        ];

        List<int> selected = NeuralMeshFrameExtractor.SelectTopKFromSparse(
            values,
            static _ => true,
            take: 2,
            distinct: true);

        Assert.Equal([2, 1], selected);
    }

    [Fact]
    public void SelectTopKFromSparse_non_distinct_keeps_duplicate_indexes()
    {
        List<SparseValue> values =
        [
            new(7, 4f),
            new(7, 3f),
            new(8, 1f),
        ];

        List<int> selected = NeuralMeshFrameExtractor.SelectTopKFromSparse(
            values,
            static _ => true,
            take: 2,
            distinct: false);

        Assert.Equal([7, 7], selected);
    }
}

public sealed class TrainingHeapSpillTests
{
    [Fact]
    public void TryPrepare_releases_heap_before_snapshot_and_writes_spill_checkpoint()
    {
        List<string> order = [];
        NeuralNetParameterSnapshot snapshot = SampleSnapshot("cafebabe");
        List<string> traces = ["frame"];

        TrainingHeapSpillPrepareResult result = TrainingHeapSpill.TryPrepare(
            () =>
            {
                order.Add("release");
                traces.Clear();
            },
            () =>
            {
                order.Add("snapshot");
                return snapshot;
            },
            Guid.Parse("d9908cb9-f58b-44cf-a41f-82bc9ec9240f"),
            NeuralModelKindChatMonitoring.Moderation,
            169);

        Assert.True(result.Succeeded);
        Assert.Equal(["release", "snapshot"], order);
        Assert.Empty(traces);
        Assert.True(TrainingSpillCheckpoint.TryParse(result.Json, out TrainingSpillCheckpoint? checkpoint));
        Assert.Equal(TrainingSpillCheckpoint.Version, checkpoint?.SchemaVersion);
        Assert.Equal(snapshot.PackedValues, checkpoint?.Parameters.PackedValues);
        Assert.Equal(TrainingStepAfterSpill.AdvanceWithoutRetry, TrainingHeapSpill.AfterOutOfMemory(true));
        Assert.Equal(TrainingStepAfterSpill.Stop, TrainingHeapSpill.AfterOutOfMemory(false));
        Assert.Equal(TrainingStepAfterSpill.Stop, TrainingHeapSpill.AfterProactiveAttempt(false));
        Assert.False(TrainingPersistencePolicy.AllowsMidRunSql(isEmergencyHeapSpill: false));
    }

    [Fact]
    public void TryPrepare_is_false_when_snapshot_fails()
    {
        bool released = false;
        TrainingHeapSpillPrepareResult result = TrainingHeapSpill.TryPrepare(
            () => released = true,
            () => null,
            Guid.NewGuid(),
            NeuralModelKindChatMonitoring.Tutoring,
            3);

        Assert.True(released);
        Assert.False(result.Succeeded);
        Assert.Null(result.Json);
    }

    [Fact]
    public void TryRestore_loads_spill_checkpoint_packed_values()
    {
        NeuralNetParameterSnapshot spilled = SampleSnapshot("deadbeef");
        string json = TrainingSpillCheckpoint.Serialize(
            Guid.NewGuid(),
            NeuralModelKindChatMonitoring.Moderation,
            12,
            spilled);

        NeuralNetParameterSnapshot? loaded = null;
        Assert.True(TrainingHeapSpill.TryRestore(json, snapshot => loaded = snapshot));
        Assert.Equal(spilled.PackedValues, loaded?.PackedValues);
        Assert.True(TrainingHeapSpill.ShouldKeepSpillCheckpoint(json));
        Assert.False(TrainingHeapSpill.ShouldKeepSpillCheckpoint("{ \"schemaVersion\": \"2.0\" }"));
    }

    [Fact]
    public void BoundAfterSpill_clears_mesh_and_trims_feeds()
    {
        NeuralNetTrainingLiveProgress progress = new(
            Guid.NewGuid(),
            "Continuous training",
            TicketsRequested: 10,
            TicketsGenerated: 10,
            TicketsProcessed: 10,
            MessagesProcessed: 10,
            ExamplesPersisted: 0,
            AuditsCompleted: 0,
            ActiveChatMonitoringKind: "Moderation",
            LatestTrainingLlmSummary: "x",
            LatestAuditFeedback: "y",
            LatestLossSummary: null,
            GeneratorHints: Enumerable.Range(0, 20).Select(i => $"h{i}").ToList(),
            AuditFeedbackFeed: Enumerable.Range(0, 20).Select(i => $"a{i}").ToList(),
            CurrentEvaluationData: "blob",
            WeightUpdateFeed: Enumerable.Range(0, 500).Select(i => $"Δw {i}").ToList(),
            PathTone: "forward",
            LayerWidths: [2, 3],
            LayerLabels: ["in", "out"],
            ActiveNodeIndexes: [1, 2, 3],
            ActiveEdgeParameterIndexes: [4, 5],
            UpdatedAtUtc: DateTime.UtcNow,
            ActiveLayerIndex: 1);

        NeuralNetTrainingLiveProgress bounded = TrainingHeapSpill.BoundAfterSpill(progress, 169);
        Assert.Equal("Heap spill · checkpoint written", bounded.Phase);
        Assert.Empty(bounded.WeightUpdateFeed);
        Assert.Empty(bounded.ActiveNodeIndexes);
        Assert.Empty(bounded.ActiveEdgeParameterIndexes);
        Assert.Equal(8, bounded.AuditFeedbackFeed.Count);
        Assert.Equal(8, bounded.GeneratorHints.Count);
        Assert.DoesNotContain("replay", bounded.LatestTrainingLlmSummary ?? "", StringComparison.OrdinalIgnoreCase);

        NeuralNetTrainingLiveProgress cancelled = TrainingHeapSpill.BoundAfterCancel(progress);
        Assert.Equal("Cancelled", cancelled.Phase);
        Assert.False(TrainingPersistencePolicy.IsActiveLivePhase(cancelled.Phase));
        Assert.Empty(cancelled.WeightUpdateFeed);
    }

    [Fact]
    public void CapFeed_bounds_weight_update_lines()
    {
        List<string> lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToList();
        List<string> capped = TrainingHeapSpill.CapFeed(lines, max: 24);
        Assert.Equal(24, capped.Count);
        Assert.Contains("more", capped[^1], StringComparison.Ordinal);
    }

    private static NeuralNetParameterSnapshot SampleSnapshot(string checksum) =>
        new(3, 4, "ieee754-float32-le", "dense-base64", 2, "AAAAAA==", checksum);
}
