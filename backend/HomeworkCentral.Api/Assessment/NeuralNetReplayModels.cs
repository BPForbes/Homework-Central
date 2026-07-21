namespace HomeworkCentral.Api.Assessment;

public enum NeuralTrainingMode { Both, Moderation, Tutoring }
public enum NeuralTrainingTraceDetail { Full, Compact }
public enum ReplayCompletionStatus { Completed, Cancelled, Failed, Partial }
public enum ReplayPhase { Llm1Input, InitialForward, Llm2Evaluation, VoteResolution, EpochForward, LossCalculation, BackwardPropagation, ParameterUpdate, PostUpdateForward, FinalVerdict }
public enum ReplayPayloadKind { Llm1Input, Forward, Evaluation, Loss, Backpropagation, ParameterUpdate, VoteGeneration, VoteEvaluation, VoteSampling, FinalVerdict }
public enum ReplayParameterKind { Weight, Bias }

public sealed record SparseValue(int Index, float Value);
public sealed record ReplayStringTable(IReadOnlyList<string> Values);
public sealed record ReplayNode(int Index, string NodeId, string LayerId, string Label, int? FeatureIndex, bool Trainable, long CreatedAtSequence = 0, long? DisabledAtSequence = null, long? RemovedAtSequence = null);
public sealed record ReplayParameter(int Index, string ParameterId, ReplayParameterKind Kind, int? SourceNodeIndex, int TargetNodeIndex, bool Trainable, long CreatedAtSequence = 0, long? FrozenAtSequence = null, long? RemovedAtSequence = null);
public sealed record ReplayEdge(int Index, string EdgeId, int SourceNodeIndex, int TargetNodeIndex, int ParameterIndex, long CreatedAtSequence = 0, long? DisabledAtSequence = null, long? RemovedAtSequence = null);
public sealed record NeuralNetTopologySnapshot(string ModelVersion, IReadOnlyList<ReplayNode> Nodes, IReadOnlyList<ReplayEdge> Edges, IReadOnlyList<ReplayParameter> Parameters);
public sealed record FeatureSource(string Token, string SourceField, int TokenOccurrenceIndex, uint Hash, float AddedValue);
public sealed record FeatureActivation(int FeatureIndex, float Value, IReadOnlyList<FeatureSource> Sources);
public sealed record ForwardPropagationTrace(IReadOnlyList<FeatureActivation> Features, IReadOnlyList<SparseValue> NodePreActivations, IReadOnlyList<SparseValue> NodeActivations, IReadOnlyList<SparseValue> EdgeContributions, IReadOnlyList<SparseValue> BiasContributions, float EvidenceLogit, float RelevanceLogit, float EvidenceProbability, float RelevanceProbability, float Confidence);
public sealed record LossTrace(string Function, float EvidenceLoss, float RelevanceLoss, float RegularizationLoss, float TotalLoss, int SampleCount = 1, float CategoryLoss = 0f);
public sealed record GradientHealth(bool VanishingDetected, bool ExplodingDetected, float VanishingThreshold, float ExplodingThreshold, float MaximumAbsoluteGradient, float MinimumNonZeroAbsoluteGradient);
public sealed record BackpropagationTrace(IReadOnlyList<SparseValue> ActivationGradients, IReadOnlyList<SparseValue> PreActivationGradients, IReadOnlyList<SparseValue> WeightGradients, IReadOnlyList<SparseValue> BiasGradients, float GradientL2Norm, GradientHealth Health);
public sealed record ParameterDelta(int ParameterIndex, float ValueBefore, float Gradient, float Delta, float ValueAfter);
public sealed record ParameterUpdateTrace(float LearningRate, string Optimizer, IReadOnlyList<ParameterDelta> Parameters);
public sealed record TrainingIterationReplay(int Epoch, ForwardPropagationTrace BeforeUpdate, LossTrace LossBeforeUpdate, BackpropagationTrace Backward, ParameterUpdateTrace Update, ForwardPropagationTrace AfterUpdate, LossTrace LossAfterUpdate);
/// <summary>One training pass. <see cref="BatchSize"/> &gt; 1 means iterations used 3B1B-style average cost C = (1/n) Σ C_x.</summary>
public sealed record TrainingPassTrace(IReadOnlyList<TrainingIterationReplay> Iterations, int BatchSize = 1, float FinalAverageCost = 0f);
public sealed record NeuralNetParameterSnapshot(long? CanonicalGeneration, int LocalRevision, string NumericFormat, string Encoding, int ParameterCount, string PackedValues, string Checksum);
public sealed record ReplayIntegrity(string CanonicalizationVersion, string HashAlgorithm, string TopologyChecksum, string InitialStateChecksum, string FinalStateChecksum, string ReportChecksum);
public sealed record ReplayFrame(long Sequence, ReplayPhase Phase, ReplayPayloadKind PayloadKind, int TicketIndex, int PassIndex, int? MessageIndex, int? Epoch, DateTimeOffset? CapturedAt, int PayloadIndex);
public sealed record ReplayFailure(string Stage, string ErrorCode, string SanitizedMessage);
public sealed record TrainingProvenance(string ModelVersion, string FeatureEncoderVersion, string LossFunctionVersion, string Optimizer, float LearningRate, int EpochCount, string RandomAlgorithm, int RandomSeed, string ReportProducerVersion);

public sealed record Llm1InstructionTrace(int RequirementStringIndex, int ContextStringIndex, int MessageStringIndex, int ChannelStringIndex, int AuthorRoleStringIndex, bool IsDistractor, float ChannelRelevance, string GeneratorModel, string GeneratorPromptVersion);
public sealed record Llm2EvaluationTrace(bool ParseSucceeded, bool TeacherReturnedLgtm, bool IsLgtm, float TargetEvidence, float TargetRelevance, float EvaluationScore, float EvaluatorApprovalEstimate, float EvaluatorConfidence, IReadOnlyList<int> FailedCriteriaStringIndices, int FeedbackStringIndex, string EvaluatorModel, string EvaluatorPromptVersion);
public sealed record FinalVerdictTrace(bool Accepted, int ReasonStringIndex, float EvaluationScore, float AcceptanceThreshold, int CompletedEpochs, int InitialForwardPayloadIndex, int? FinalForwardPayloadIndex);
public sealed record TrainingPassReplay(int PassIndex, int MessageIndex, int InputPayloadIndex, int InitialForwardPayloadIndex, int EvaluationPayloadIndex, int VoteGenerationPayloadIndex, int VoteEvaluationPayloadIndex, int VoteSamplingPayloadIndex, IReadOnlyList<TrainingIterationReplay> Iterations, int? FinalForwardPayloadIndex, NeuralNetParameterSnapshot? PostPassParameters);
public sealed record TrainingMessageReplay(int MessageIndex, int AuthorIdStringIndex, int AuthorRoleStringIndex, int ChannelStringIndex, bool IsDistractor, float ChannelRelevance, IReadOnlyList<TrainingPassReplay> Passes);
public sealed record TrainingTicketReplay(int TicketIndex, int CategoryStringIndex, int RequirementStringIndex, int ContextStringIndex, IReadOnlyList<TrainingMessageReplay> Messages);
public sealed record ReplayPayloadCollections(IReadOnlyList<Llm1InstructionTrace> Inputs, IReadOnlyList<ForwardPropagationTrace> ForwardPasses, IReadOnlyList<Llm2EvaluationTrace> Evaluations, IReadOnlyList<LossTrace> Losses, IReadOnlyList<BackpropagationTrace> Backpropagations, IReadOnlyList<ParameterUpdateTrace> ParameterUpdates, IReadOnlyList<FinalVerdictTrace> FinalVerdicts, IReadOnlyList<SyntheticVoteGenerationTrace> VoteGeneration, IReadOnlyList<SyntheticVoteEvaluationTrace> VoteEvaluation, IReadOnlyList<SyntheticVoteSamplingTrace> VoteSampling);
public sealed record NeuralNetReplayReportV2(string SchemaVersion, Guid SessionId, ReplayCompletionStatus CompletionStatus, NeuralNetTopologySnapshot Topology, ReplayStringTable Strings, TrainingProvenance Provenance, NeuralNetParameterSnapshot InitialParameters, IReadOnlyList<TrainingTicketReplay> Tickets, IReadOnlyList<ReplayFrame> Frames, ReplayPayloadCollections Payloads, NeuralNetParameterSnapshot FinalParameters, ReplayIntegrity Integrity, ReplayFailure? Failure);
public sealed record PromotionValidationResult(bool Passed, IReadOnlyList<string> PassedRules, IReadOnlyList<string> FailedRules, string BaseChecksum, string FinalChecksum, int AppliedExampleCount, int CompletedEpochCount);
public sealed record ModelPromotionReplay(Guid PromotionId, Guid SourceSessionId, string SourceReportChecksum, ReplayStringTable Strings, NeuralNetParameterSnapshot InitialParameters, IReadOnlyList<Guid> AppliedExampleIds, NeuralNetParameterSnapshot FinalParameters, PromotionValidationResult Validation, ReplayIntegrity Integrity);

public sealed record SyntheticCommunityIntent(float ProposedApproval, int ProposedVoterCount, float Controversy, IReadOnlyList<string> Reasons);
public sealed record SyntheticVoteGenerationTrace(string ScenarioBucket, float GeneratorProposedApproval, int GeneratorProposedVoterCount, IReadOnlyList<string> GeneratorReasons, string GeneratorPromptVersion);
public sealed record SyntheticVoteEvaluationTrace(float EvaluatorApprovalEstimate, float EvaluatorConfidence, IReadOnlyList<string> EvaluatorReasons, bool ProposalPlausible, float ProposalDifference, string EvaluatorPromptVersion);
public sealed record SyntheticVoteSamplingTrace(float FinalApprovalProbability, int VoterCount, int Upvotes, int Downvotes, string SamplingMethod, string SamplingPolicyVersion, string RandomAlgorithm, int RandomSeed);
