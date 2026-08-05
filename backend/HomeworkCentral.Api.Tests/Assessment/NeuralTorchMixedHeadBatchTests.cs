using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Tests.Assessment;

public class NeuralTorchMixedHeadBatchTests
{
    public NeuralTorchMixedHeadBatchTests()
    {
        NeuralTorchRuntime.Configure(new NeuralNetTrainingOptions
        {
            PreferTorchAccelerator = true,
            TorchDevice = "cpu",
        });
    }

    [Fact]
    public void Torch_runtime_binds_cpu_libtorch()
    {
        Assert.True(NeuralTorchRuntime.TryEnsureReady());
        Assert.True(NeuralTorchRuntime.IsAvailable);
        Assert.Equal("torch-cpu", NeuralTorchRuntime.BackendLabel);
    }

    [Fact]
    public void Silent_training_uses_torch_optimizer_label()
    {
        Assert.True(NeuralTorchRuntime.TryEnsureReady());
        using ModerationEvidenceScorerNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new(
            "Monitor for cussing.",
            "Prior conduct was reported.",
            "That was a rude curse.",
            0, .9f, .4f, .5f);
        TrainingPassTrace silent = model.TrainWithTrace(
            new ChatMonitoringNeuralModelTrainingExample(
                input,
                new ChatMonitoringNeuralModelTargets(
                    .95f,
                    .9f,
                    ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "profanity")),
                "profanity"),
            epochs: 2,
            detail: NeuralTrainingTraceDetail.None);
        Assert.Empty(silent.Iterations);

        TrainingPassTrace compact = model.TrainWithTrace(
            new ChatMonitoringNeuralModelTrainingExample(
                input,
                new ChatMonitoringNeuralModelTargets(
                    .95f,
                    .9f,
                    ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "profanity")),
                "profanity"),
            epochs: 2,
            detail: NeuralTrainingTraceDetail.Compact);
        Assert.NotEmpty(compact.Iterations);
        Assert.Contains("torch-cpu", compact.Iterations[^1].Update.Optimizer);
    }

    [Fact]
    public void Full_trace_stays_on_mathnet_path()
    {
        Assert.True(NeuralTorchRuntime.TryEnsureReady());
        using ModerationEvidenceScorerNeuralNet model = new();
        ChatMonitoringNeuralModelInput input = new(
            "Monitor for cussing.",
            "Prior conduct was reported.",
            "That was a rude curse.",
            0, .9f, .4f, .5f);
        TrainingPassTrace full = model.TrainWithTrace(
            new ChatMonitoringNeuralModelTrainingExample(
                input,
                new ChatMonitoringNeuralModelTargets(
                    .95f,
                    .9f,
                    ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Moderation, "profanity")),
                "profanity"),
            epochs: 1,
            detail: NeuralTrainingTraceDetail.Full);
        Assert.Single(full.Iterations);
        Assert.DoesNotContain("torch", full.Iterations[0].Update.Optimizer);
    }

    [Fact]
    public void Cascade_compact_still_chain_rules_with_torch()
    {
        Assert.True(NeuralTorchRuntime.TryEnsureReady());
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
            new(
                input,
                new ChatMonitoringNeuralModelTargets(
                    .95f,
                    .9f,
                    ChatMonitoringCategoryTaxonomy.IndexOf(NeuralModelKindChatMonitoring.Tutoring, "tutoring-science")),
                "tutoring-science"),
            epochs: 4,
            detail: NeuralTrainingTraceDetail.Compact);
        Assert.Contains("cascade-chain-rule", trace.Iterations[0].Update.Optimizer);
        Assert.Contains("torch-cpu", trace.Iterations[0].Update.Optimizer);
        Assert.NotEqual(routerBefore, tutoring.RouterParameterL2Norm);
    }
}
