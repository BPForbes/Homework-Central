using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Assessment;

public class ChatMonitoringNeuralModelHashedMlpTests
{
    [Fact]
    public void Checkpoint_round_trip_preserves_moderation_prediction()
    {
        // Canonical checkpoints store stage-2 scorer weights; stage-1 router relearns online.
        using ModerationEvidenceScorerNeuralNet source = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for cussing.", "Prior conduct was reported.", "That was a rude curse.", 0, .9f, .4f, .5f);
        source.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "profanity")), 8);
        NeuralNetParameterSnapshot snapshot = source.GetParameterSnapshot(4, 8);
        using ModerationEvidenceScorerNeuralNet restored = new();
        restored.LoadParameterSnapshot(snapshot);
        ChatMonitoringNeuralModelPrediction expected = source.Predict(input);
        ChatMonitoringNeuralModelPrediction actual = restored.Predict(input);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.Relevance, actual.Relevance);
        Assert.Equal(expected.Category, actual.Category);
    }

    [Fact]
    public void Multi_subject_physics_answer_gets_math_cross_support_boost()
    {
        SubjectSignalSnapshot snapshot = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Mathematics, SubjectMaskNames.Science],
            SubjectMaskNames.Science);
        Assert.Equal(1f, snapshot.ExactMatch);
        Assert.True(snapshot.CrossSubjectSupport >= .8f);
        Assert.True(snapshot.RewardScale > .9f);
        Assert.True(snapshot.EffectiveChannelRelevance > .85f);

        SubjectSignalSnapshot unrelated = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Mathematics],
            SubjectMaskNames.Art);
        Assert.True(unrelated.RewardScale < snapshot.RewardScale);
    }

    [Fact]
    public void Tutoring_cascade_enriches_with_stage1_context()
    {
        using TutoringChatMonitorNeuralNet tutoring = new();
        SubjectSignalSnapshot subjects = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Mathematics, SubjectMaskNames.Science],
            SubjectMaskNames.Science);
        ChatMonitoringNeuralModelInput input = ChatMonitoringNeuralModelInput.Create(
            "Tutor math and science applicant.",
            "Physics help thread.",
            "Use F=ma and solve for acceleration.",
            0, .5f, .5f, subjects);
        tutoring.Train(input, new ChatMonitoringNeuralModelTargets(.85f, .9f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, "tutoring-science")), 12);
        ChatMonitoringNeuralModelPrediction prediction = tutoring.Predict(input);
        Assert.False(string.IsNullOrWhiteSpace(prediction.Category));
        Assert.True(prediction.Evidence > .4f);
        Assert.Equal(86, tutoring.GetStateSnapshot().LayerWidths[0]);
        Assert.Contains(tutoring.GetTopologySnapshot().Nodes, node => node.Label == "cascade-context-0");
    }

    [Fact]
    public void Cascade_chain_rule_updates_stage1_from_stage2_loss()
    {
        using TutoringChatMonitorNeuralNet tutoring = new();
        SubjectSignalSnapshot subjects = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Mathematics, SubjectMaskNames.Science],
            SubjectMaskNames.Science);
        ChatMonitoringNeuralModelInput input = ChatMonitoringNeuralModelInput.Create(
            "Tutor math and science applicant.",
            "Physics help thread.",
            "Use F=ma and solve for acceleration.",
            0, .5f, .5f, subjects);
        float routerBefore = tutoring.RouterParameterL2Norm;
        TrainingPassTrace trace = tutoring.TrainWithTrace(
            new(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
                ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, "tutoring-science")),
                "tutoring-science"),
            epochs: 8,
            detail: NeuralTrainingTraceDetail.Compact);
        Assert.Contains("cascade-chain-rule", trace.Iterations[0].Update.Optimizer);
        Assert.NotEqual(routerBefore, tutoring.RouterParameterL2Norm);
        Assert.True(tutoring.Predict(input).Evidence > .5f);
    }

    [Fact]
    public void Moderation_cascade_chain_rule_updates_concept_router()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        ChatMonitoringNeuralModelInput input = new(
            "Monitor reportedConcept=payment-solicitation with related tip-pressure.",
            "Help thread.",
            "I know the answer, but send me $10 first.",
            0, 1f, .5f, .5f);
        float routerBefore = moderation.RouterParameterL2Norm;
        TrainingPassTrace trace = moderation.TrainWithTrace(
            new(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
                ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "payment-solicitation")),
                "payment-solicitation"),
            epochs: 8,
            detail: NeuralTrainingTraceDetail.Compact);
        Assert.Contains("cascade-chain-rule", trace.Iterations[0].Update.Optimizer);
        Assert.NotEqual(routerBefore, moderation.RouterParameterL2Norm);
        Assert.Contains(moderation.GetTopologySnapshot().Nodes, node => node.Label == "cascade-context-0");
        Assert.Contains(moderation.GetTopologySnapshot().Nodes, node => node.Label == "payment-solicitation");
    }

    [Fact]
    public void Mini_batch_average_cost_uses_sample_count_and_moves_predictions()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelTrainingExample[] batch =
        [
            new(new("Monitor for persistent-unwanted-contact.", "Insults.", "You are worthless.", 0, 1f, .6f, .5f),
                new(.95f, .9f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment")), "harassment"),
            new(new("Monitor for tip-pressure.", "Tips.", "Tips are expected for the last step.", 0, 1f, .5f, .5f),
                new(.9f, .85f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "tip-pressure")), "tip-pressure"),
            new(new("Monitor for fake-engagement spam flood.", "Flood.", "Buy coins now buy coins now.", 0, .8f, .4f, .5f),
                new(.8f, .7f, ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "spam")), "spam"),
        ];
        TrainingPassTrace trace = model.TrainMiniBatchWithTrace(batch, epochs: 20, detail: NeuralTrainingTraceDetail.Compact);
        Assert.Equal(3, trace.BatchSize);
        Assert.All(trace.Iterations, iteration =>
        {
            Assert.Equal(3, iteration.LossBeforeUpdate.SampleCount);
            Assert.True(iteration.LossBeforeUpdate.TotalLoss > 0f);
            Assert.True(iteration.LossBeforeUpdate.CategoryLoss >= 0f);
        });
        Assert.True(trace.FinalAverageCost > 0f);
        ChatMonitoringNeuralModelPrediction after = model.Predict(batch[0].Input);
        Assert.True(after.Evidence > .5f);
        Assert.False(string.IsNullOrWhiteSpace(after.Category));
        Assert.True(after.CategoryConfidence > 0f);
    }

    [Fact]
    public void Softmax_category_head_is_present_in_topology()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        NeuralNetTopologySnapshot moderationTopology = moderation.GetTopologySnapshot();
        NeuralNetTopologySnapshot tutoringTopology = tutoring.GetTopologySnapshot();
        // Moderation evidence: 86+48+72+64+56+103 = 429
        // Tutoring evidence: 86+40+56+48+40+16 = 286
        Assert.Equal(429, moderationTopology.Nodes.Count);
        Assert.Equal(286, tutoringTopology.Nodes.Count);
        Assert.Equal("hc-chat-monitoring-moderation-evidence-v8", moderationTopology.ModelVersion);
        Assert.Equal("hc-chat-monitoring-tutoring-evidence-v8", tutoringTopology.ModelVersion);
        Assert.Equal(100, ChatMonitoringModerationConcepts.Slugs.Count);
        Assert.Equal(101, ChatMonitoringCategoryTaxonomy.Moderation.Length);
        Assert.Contains(moderationTopology.Nodes, node => node.Label == "payment-solicitation");
        Assert.Contains(moderationTopology.Nodes, node => node.Label == "moderation-general");
        Assert.Contains(tutoringTopology.Nodes, node => node.Label == "tutoring-mathematics");
        Assert.Contains(tutoringTopology.Nodes, node => node.Label == "tutoring-history");
        Assert.Contains(tutoringTopology.Nodes, node => node.Label == "tutoring-computer-science");
        Assert.Contains(tutoringTopology.Nodes, node => node.Label == "tutoring-business");
        Assert.Equal([86, 48, 72, 64, 56, 103], moderation.GetStateSnapshot().LayerWidths);
        Assert.Equal([86, 40, 56, 48, 40, 16], tutoring.GetStateSnapshot().LayerWidths);
        Assert.Equal(ChatMonitoringCategoryTaxonomy.Moderation.Length + 2, moderation.GetStateSnapshot().LayerWidths[^1]);
        Assert.Equal(ChatMonitoringCategoryTaxonomy.Tutoring.Length + 2, tutoring.GetStateSnapshot().LayerWidths[^1]);
        Assert.True(moderationTopology.Edges.Count > tutoringTopology.Edges.Count);
        // Dense MLP edges exceed the old student-model cap (4096); keep within cascade-aware V2 limits.
        Assert.Equal(21544, moderationTopology.Edges.Count);
        Assert.Equal(10928, tutoringTopology.Edges.Count);
        Assert.True(moderationTopology.Edges.Count <= NeuralNetReplaySerializer.MaxEdges);
        Assert.True(tutoringTopology.Edges.Count <= NeuralNetReplaySerializer.MaxEdges);
        Assert.True(moderationTopology.Nodes.Count <= NeuralNetReplaySerializer.MaxNodes);
        Assert.True(tutoringTopology.Nodes.Count <= NeuralNetReplaySerializer.MaxNodes);
        AssertReplayValidates(moderation);
        AssertReplayValidates(tutoring);
    }

    private static void AssertReplayValidates(IChatMonitoringNeuralModelTelemetry model)
    {
        NeuralNetTopologySnapshot topology = model.GetTopologySnapshot();
        NeuralNetParameterSnapshot parameters = model.GetParameterSnapshot(null, 0);
        ReplayIntegrity placeholder = new("hc-replay-canonical-json-v1", "sha-256", "", parameters.Checksum, parameters.Checksum, "");
        NeuralNetReplayReportV2 draft = new(
            "2.0",
            Guid.NewGuid(),
            ReplayCompletionStatus.Completed,
            topology,
            new ReplayStringTable([]),
            new TrainingProvenance(topology.ModelVersion, "hashed-text-48-v1", "bce+softmax-ce-avg-v1", "momentum-mini-batch-SGD", .035f, 1, "hc-xoshiro256ss-v1", 0, "replay-v2-worker-v1"),
            parameters,
            [],
            [],
            new ReplayPayloadCollections([], [], [], [], [], [], [], [], [], []),
            parameters,
            placeholder,
            null);
        ReplayIntegrity integrity = NeuralNetReplaySerializer.CreateIntegrity(topology, parameters, parameters, NeuralNetReplaySerializer.Serialize(draft));
        NeuralNetReplaySerializer.Validate(draft with { Integrity = integrity });
    }

    [Fact]
    public void Moderation_taxonomy_normalizes_legacy_and_exposes_related_concepts()
    {
        Assert.Equal("persistent-unwanted-contact",
            ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Moderation, "harassment"));
        Assert.Equal("violent-intent",
            ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Moderation, "threat"));
        Assert.Equal("payment-solicitation",
            ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Moderation, "payment-solicitation"));
        Assert.Contains("tip-pressure", ChatMonitoringModerationConcepts.RelatedConcepts("payment-solicitation"));
        Assert.Equal(ChatMonitoringModerationConcepts.Families.Financial,
            ChatMonitoringModerationConcepts.FamilyOf("tip-solicitation"));
    }

    [Fact]
    public void Tutoring_taxonomy_covers_every_general_subject_tag()
    {
        foreach (SubjectExpertiseCategory subject in SubjectExpertiseCatalog.Categories)
        {
            string slug = ChatMonitoringCategoryTaxonomy.SubjectToTutoringSlug(subject.ExpertiseMaskName);
            Assert.Contains(slug, ChatMonitoringCategoryTaxonomy.Tutoring);
            Assert.Equal(slug, ChatMonitoringCategoryTaxonomy.Label(
                NeuralModelKindChatMonitoring.Tutoring,
                ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, subject.ExpertiseMaskName)));
        }

        Assert.Equal("tutoring-mathematics", ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Tutoring, "tutoring-math"));
        Assert.Equal("tutoring-languages", ChatMonitoringCategoryTaxonomy.NormalizeCategory(NeuralModelKindChatMonitoring.Tutoring, "tutoring-english"));
    }

    [Fact]
    public void Compact_training_records_loss_without_parameter_deltas()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for persistent-unwanted-contact.", "Repeated insults.", "You are worthless.", 0, 1f, .6f, .5f);
        TrainingPassTrace trace = model.TrainWithTrace(
            new(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
                ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment")), "harassment"),
            epochs: 40,
            detail: NeuralTrainingTraceDetail.Compact,
            evidenceTolerance: 0.2f,
            relevanceTolerance: 0.2f,
            lossStopThreshold: 1.2f);
        Assert.True(trace.Iterations.Count >= 1);
        Assert.True(trace.Iterations.Count <= 40);
        Assert.All(trace.Iterations, iteration => Assert.Empty(iteration.Update.Parameters));
        Assert.True(trace.Iterations[^1].LossAfterUpdate.TotalLoss > 0f);
    }

    [Fact]
    public void Lineage_vector_keys_are_stable_per_monitor()
    {
        Assert.Equal("chat-monitoring-moderation", ChatMonitoringVectorKeys.LineagePositionId(NeuralModelKindChatMonitoring.Moderation));
        Assert.Equal("chat-monitoring-tutoring", ChatMonitoringVectorKeys.LineagePositionId(NeuralModelKindChatMonitoring.Tutoring));
    }

    [Fact]
    public void Specialized_models_have_separate_fixed_topologies_and_real_updates()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelInput input = new("Monitor math tutoring.", "A student asked for help.", "The quadratic formula is useful here.", .2f, .9f, .5f, .4f);
        TrainingPassTrace trace = tutoring.TrainWithTrace(new(input, new ChatMonitoringNeuralModelTargets(.8f, .9f, 0), "tutoring-mathematics"), 2);
        NeuralNetTopologySnapshot moderationTopology = moderation.GetTopologySnapshot();
        NeuralNetTopologySnapshot tutoringTopology = tutoring.GetTopologySnapshot();
        Assert.Equal(2, trace.Iterations.Count);
        Assert.Equal(286, tutoringTopology.Nodes.Count);
        Assert.Equal(429, moderationTopology.Nodes.Count);
        Assert.Contains(tutoringTopology.Nodes, node => node.LayerId == "learning-thread-history");
        Assert.Contains(moderationTopology.Nodes, node => node.LayerId == "behavior-history");
        Assert.All(trace.Iterations, iteration => Assert.NotEmpty(iteration.Update.Parameters));
    }

    [Fact]
    public void Factory_resolves_independent_models_for_training_modes()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelFactory factory = new(moderation, tutoring);
        IReadOnlyList<IChatMonitoringNeuralModel> both = factory.Resolve(NeuralTrainingMode.Both);
        IReadOnlyList<IChatMonitoringNeuralModel> moderationOnly = factory.Resolve(NeuralTrainingMode.Moderation);
        IReadOnlyList<IChatMonitoringNeuralModel> tutoringOnly = factory.Resolve(NeuralTrainingMode.Tutoring);
        Assert.Equal(2, both.Count);
        Assert.NotSame(both[0], both[1]);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, both[0].Kind);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, both[1].Kind);
        Assert.Single(moderationOnly);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, moderationOnly[0].Kind);
        Assert.Single(tutoringOnly);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, tutoringOnly[0].Kind);
    }

    [Fact]
    public void Moderation_and_tutoring_networks_do_not_share_weights()
    {
        using ModerationChatMonitorNeuralNet moderation = new();
        using TutoringChatMonitorNeuralNet tutoring = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment.", "Repeated insults.", "You are worthless.", 0, 1f, .6f, .5f);
        moderation.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
            ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment")), 20);
        ChatMonitoringNeuralModelPrediction moderationAfter = moderation.Predict(input);
        ChatMonitoringNeuralModelPrediction tutoringUntrained = tutoring.Predict(input);
        Assert.NotEqual(moderationAfter.Evidence, tutoringUntrained.Evidence);
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, moderationAfter.ChatMonitoringKind);
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, tutoringUntrained.ChatMonitoringKind);
    }

    [Fact]
    public void Training_moves_predictions_toward_targets()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment.", "Repeated insults in chat.", "You are worthless.", 0, 1f, .6f, .5f);
        ChatMonitoringNeuralModelPrediction before = model.Predict(input);
        model.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
            ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment")), 40);
        ChatMonitoringNeuralModelPrediction after = model.Predict(input);
        Assert.True(after.Evidence > before.Evidence);
        Assert.True(after.Relevance > before.Relevance);
    }

    [Fact]
    public void Support_similarity_raises_confidence_after_training()
    {
        using ModerationChatMonitorNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new("Monitor for harassment and insults.", "Prior insults.", "You are worthless.", 0, 1f, .6f, .5f);
        ChatMonitoringNeuralModelPrediction before = model.Predict(input);
        model.Train(input, new ChatMonitoringNeuralModelTargets(.95f, .9f,
            ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "harassment")), 30);
        ChatMonitoringNeuralModelPrediction after = model.Predict(input);
        Assert.True(after.Confidence >= before.Confidence);
        Assert.False(string.IsNullOrWhiteSpace(after.Category));
        Assert.False(string.IsNullOrWhiteSpace(after.Reasoning));
    }

    [Fact]
    public void Ticket_context_routes_tutoring_filters_to_tutoring_monitor()
    {
        Assert.Equal(NeuralModelKindChatMonitoring.Tutoring, ChatMonitoringTicketContext.ResolveKind("Tutor application competency", "learning"));
        Assert.Equal(NeuralModelKindChatMonitoring.Moderation, ChatMonitoringTicketContext.ResolveKind("Profanity filter", "moderation"));
    }

    [Fact]
    public void Community_sampling_and_promotion_leasing_are_deterministic()
    {
        SyntheticCommunityIntent intent = new(.9f, 30, .1f, ["helpful"]);
        SyntheticCommunityResolution first = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        SyntheticCommunityResolution second = SyntheticCommunitySignalResolver.Resolve(intent, .7f, .8f, .6f, .9f, 123);
        DateTime now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(first.Sampling, second.Sampling);
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Pending", null, now));
        Assert.True(NeuralNetTrainingPromoter.CanClaim("Promoting", now.AddSeconds(-1), now));
        Assert.False(NeuralNetTrainingPromoter.CanClaim("Promoted", null, now));
    }

    [Fact]
    public void Compact_training_traces_stay_json_serializable_without_non_finite_numbers()
    {
        // Unbounded logits previously overflowed to ±Infinity and caused
        // System.Text.Json ArgumentException when persisting session/replay reports.
        using TutoringChatMonitorNeuralNet model = new();
        SubjectSignalSnapshot subjects = ChatMonitoringSubjectSignals.Resolve(
            [SubjectMaskNames.Mathematics, SubjectMaskNames.Science],
            SubjectMaskNames.Science);
        ChatMonitoringNeuralModelInput input = ChatMonitoringNeuralModelInput.Create(
            "Tutor math and science applicant.",
            "Physics help thread with repeated strong evidence signals.",
            "Use F=ma and solve for acceleration carefully with units.",
            communityVote: 1f,
            threadContinuity: 1f,
            priorScore: 1f,
            subjects);
        List<ChatMonitoringNeuralModelTrainingExample> batch =
        [
            new(input, new ChatMonitoringNeuralModelTargets(.99f, .99f,
                ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, "tutoring-science")),
                "tutoring-science"),
            new(input with { Message = "Explain Newton second law again with free-body diagrams." },
                new ChatMonitoringNeuralModelTargets(.95f, .9f,
                    ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, "tutoring-science")),
                "tutoring-science"),
        ];

        TrainingPassTrace trace = model.TrainMiniBatchWithTrace(
            batch,
            epochs: 40,
            detail: NeuralTrainingTraceDetail.Compact,
            evidenceTolerance: 0f,
            relevanceTolerance: 0f,
            lossStopThreshold: 0f);

        Assert.True(float.IsFinite(trace.FinalAverageCost));
        foreach (TrainingIterationReplay iteration in trace.Iterations)
        {
            Assert.True(float.IsFinite(iteration.BeforeUpdate.EvidenceLogit));
            Assert.True(float.IsFinite(iteration.BeforeUpdate.RelevanceLogit));
            Assert.True(float.IsFinite(iteration.AfterUpdate.EvidenceLogit));
            Assert.True(float.IsFinite(iteration.LossBeforeUpdate.TotalLoss));
            Assert.True(float.IsFinite(iteration.LossAfterUpdate.TotalLoss));
            Assert.True(float.IsFinite(iteration.Backward.GradientL2Norm));
        }

        string json = System.Text.Json.JsonSerializer.Serialize(
            trace,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("Infinity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NaN", json, StringComparison.OrdinalIgnoreCase);
    }
}
