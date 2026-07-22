namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Downloaded V2 neural-net replay / promotion JSON contracts used by admin training sessions
/// and the frontend replay viewer. Schema shape is owned by
/// <see cref="NeuralNetReplaySerializer"/>; import limits live there and in
/// <c>frontend/src/utils/neuralNetReplay.ts</c>. See docs/tickets.md.
/// </summary>
public enum NeuralTrainingMode { Both, Moderation, Tutoring }

/// <summary>How densely forward/backprop frames are retained in a training replay.</summary>
public enum NeuralTrainingTraceDetail { Full, Compact }

/// <summary>Terminal or interrupted outcome persisted on a V2 replay report.</summary>
public enum ReplayCompletionStatus { Completed, Cancelled, Failed, Partial }

/// <summary>Ordered training/evaluation stage recorded on each <see cref="ReplayFrame"/>.</summary>
public enum ReplayPhase { Llm1Input, InitialForward, Llm2Evaluation, VoteResolution, EpochForward, LossCalculation, BackwardPropagation, ParameterUpdate, PostUpdateForward, FinalVerdict }

/// <summary>Which payload collection a frame indexes into inside <see cref="ReplayPayloadCollections"/>.</summary>
public enum ReplayPayloadKind { Llm1Input, Forward, Evaluation, Loss, Backpropagation, ParameterUpdate, VoteGeneration, VoteEvaluation, VoteSampling, FinalVerdict }

/// <summary>Trainable parameter taxonomy inside topology snapshots.</summary>
public enum ReplayParameterKind { Weight, Bias }

/// <summary>Sparse tensor cell used in forward/backprop traces (index into a dense layer vector).</summary>
public sealed record SparseValue(int Index, float Value);

/// <summary>Deduplicated string pool; payload fields store indices into <see cref="Values"/>.</summary>
public sealed record ReplayStringTable(IReadOnlyList<string> Values);

/// <summary>Graph node in a hashed-MLP topology snapshot (feature or hidden unit).</summary>
public sealed record ReplayNode(int Index, string NodeId, string LayerId, string Label, int? FeatureIndex, bool Trainable, long CreatedAtSequence = 0, long? DisabledAtSequence = null, long? RemovedAtSequence = null);

/// <summary>Weight or bias parameter referenced by edges; sequence fields track structural mutations.</summary>
public sealed record ReplayParameter(int Index, string ParameterId, ReplayParameterKind Kind, int? SourceNodeIndex, int TargetNodeIndex, bool Trainable, long CreatedAtSequence = 0, long? FrozenAtSequence = null, long? RemovedAtSequence = null);

/// <summary>Directed connection from a source node through a parameter into a target node.</summary>
public sealed record ReplayEdge(int Index, string EdgeId, int SourceNodeIndex, int TargetNodeIndex, int ParameterIndex, long CreatedAtSequence = 0, long? DisabledAtSequence = null, long? RemovedAtSequence = null);

/// <summary>Immutable topology captured at session start for deterministic replay import.</summary>
public sealed record NeuralNetTopologySnapshot(string ModelVersion, IReadOnlyList<ReplayNode> Nodes, IReadOnlyList<ReplayEdge> Edges, IReadOnlyList<ReplayParameter> Parameters);

/// <summary>One hashed-feature contribution source used when explaining sparse activations.</summary>
public sealed record FeatureSource(string Token, string SourceField, int TokenOccurrenceIndex, uint Hash, float AddedValue);

/// <summary>Active feature bucket after encoding, with provenance sources for audit UI.</summary>
public sealed record FeatureActivation(int FeatureIndex, float Value, IReadOnlyList<FeatureSource> Sources);

/// <summary>
/// One forward pass. Probabilities are sigmoid outputs in <c>[0, 1]</c>; logits are clamped
/// before JSON serialization so ±Infinity cannot mark a session Failed.
/// </summary>
public sealed record ForwardPropagationTrace(IReadOnlyList<FeatureActivation> Features, IReadOnlyList<SparseValue> NodePreActivations, IReadOnlyList<SparseValue> NodeActivations, IReadOnlyList<SparseValue> EdgeContributions, IReadOnlyList<SparseValue> BiasContributions, float EvidenceLogit, float RelevanceLogit, float EvidenceProbability, float RelevanceProbability, float Confidence);

/// <summary>Loss breakdown for evidence/relevance BCE plus optional category and regularization terms.</summary>
public sealed record LossTrace(string Function, float EvidenceLoss, float RelevanceLoss, float RegularizationLoss, float TotalLoss, int SampleCount = 1, float CategoryLoss = 0f);

/// <summary>Gradient explosion/vanishing diagnostics captured during backprop.</summary>
public sealed record GradientHealth(bool VanishingDetected, bool ExplodingDetected, float VanishingThreshold, float ExplodingThreshold, float MaximumAbsoluteGradient, float MinimumNonZeroAbsoluteGradient);

/// <summary>Backward pass gradients keyed as sparse layer vectors, plus L2 norm and health flags.</summary>
public sealed record BackpropagationTrace(IReadOnlyList<SparseValue> ActivationGradients, IReadOnlyList<SparseValue> PreActivationGradients, IReadOnlyList<SparseValue> WeightGradients, IReadOnlyList<SparseValue> BiasGradients, float GradientL2Norm, GradientHealth Health);

/// <summary>Single parameter change applied by the optimizer for one update step.</summary>
public sealed record ParameterDelta(int ParameterIndex, float ValueBefore, float Gradient, float Delta, float ValueAfter);

/// <summary>Optimizer step applied after a loss/backprop pair.</summary>
public sealed record ParameterUpdateTrace(float LearningRate, string Optimizer, IReadOnlyList<ParameterDelta> Parameters);

/// <summary>One epoch iteration: forward → loss → backprop → update → post-update forward/loss.</summary>
public sealed record TrainingIterationReplay(int Epoch, ForwardPropagationTrace BeforeUpdate, LossTrace LossBeforeUpdate, BackpropagationTrace Backward, ParameterUpdateTrace Update, ForwardPropagationTrace AfterUpdate, LossTrace LossAfterUpdate);

/// <summary>One training pass. <see cref="BatchSize"/> &gt; 1 means iterations used 3B1B-style average cost C = (1/n) Σ C_x.</summary>
public sealed record TrainingPassTrace(IReadOnlyList<TrainingIterationReplay> Iterations, int BatchSize = 1, float FinalAverageCost = 0f);

/// <summary>
/// Packed float32 weights as Base64 (<see cref="PackedValues"/>) with a checksum used for
/// promotion validation and canonical checkpoint publish.
/// </summary>
public sealed record NeuralNetParameterSnapshot(long? CanonicalGeneration, int LocalRevision, string NumericFormat, string Encoding, int ParameterCount, string PackedValues, string Checksum);

/// <summary>End-to-end integrity hashes for topology, parameter states, and the report body.</summary>
public sealed record ReplayIntegrity(string CanonicalizationVersion, string HashAlgorithm, string TopologyChecksum, string InitialStateChecksum, string FinalStateChecksum, string ReportChecksum);

/// <summary>Timeline entry pointing at a payload index inside <see cref="ReplayPayloadCollections"/>.</summary>
public sealed record ReplayFrame(long Sequence, ReplayPhase Phase, ReplayPayloadKind PayloadKind, int TicketIndex, int PassIndex, int? MessageIndex, int? Epoch, DateTimeOffset? CapturedAt, int PayloadIndex);

/// <summary>Sanitized failure recorded when a session ends as Failed/Partial without crashing the API.</summary>
public sealed record ReplayFailure(string Stage, string ErrorCode, string SanitizedMessage);

/// <summary>Training hyper-parameters and version stamps reproduced by the replay viewer.</summary>
public sealed record TrainingProvenance(string ModelVersion, string FeatureEncoderVersion, string LossFunctionVersion, string Optimizer, float LearningRate, int EpochCount, string RandomAlgorithm, int RandomSeed, string ReportProducerVersion);

/// <summary>LLM-1 synthetic instruction inputs; string fields are indices into <see cref="ReplayStringTable"/>.</summary>
public sealed record Llm1InstructionTrace(int RequirementStringIndex, int ContextStringIndex, int MessageStringIndex, int ChannelStringIndex, int AuthorRoleStringIndex, bool IsDistractor, float ChannelRelevance, string GeneratorModel, string GeneratorPromptVersion);

/// <summary>LLM-2 teacher evaluation of a student forward pass; string fields are string-table indices.</summary>
public sealed record Llm2EvaluationTrace(bool ParseSucceeded, bool TeacherReturnedLgtm, bool IsLgtm, float TargetEvidence, float TargetRelevance, float EvaluationScore, float EvaluatorApprovalEstimate, float EvaluatorConfidence, IReadOnlyList<int> FailedCriteriaStringIndices, int FeedbackStringIndex, string EvaluatorModel, string EvaluatorPromptVersion);

/// <summary>Acceptance decision after synthetic passes complete for one message.</summary>
public sealed record FinalVerdictTrace(bool Accepted, int ReasonStringIndex, float EvaluationScore, float AcceptanceThreshold, int CompletedEpochs, int InitialForwardPayloadIndex, int? FinalForwardPayloadIndex);

/// <summary>Per-message training pass with payload indices into the shared collections.</summary>
public sealed record TrainingPassReplay(int PassIndex, int MessageIndex, int InputPayloadIndex, int InitialForwardPayloadIndex, int EvaluationPayloadIndex, int VoteGenerationPayloadIndex, int VoteEvaluationPayloadIndex, int VoteSamplingPayloadIndex, IReadOnlyList<TrainingIterationReplay> Iterations, int? FinalForwardPayloadIndex, NeuralNetParameterSnapshot? PostPassParameters);

/// <summary>Synthetic ticket message and the passes run against it.</summary>
public sealed record TrainingMessageReplay(int MessageIndex, int AuthorIdStringIndex, int AuthorRoleStringIndex, int ChannelStringIndex, bool IsDistractor, float ChannelRelevance, IReadOnlyList<TrainingPassReplay> Passes);

/// <summary>Synthetic ticket scenario containing ordered messages for one training session ticket.</summary>
public sealed record TrainingTicketReplay(int TicketIndex, int CategoryStringIndex, int RequirementStringIndex, int ContextStringIndex, IReadOnlyList<TrainingMessageReplay> Messages);

/// <summary>Payload bags indexed by <see cref="ReplayFrame.PayloadIndex"/> for each <see cref="ReplayPayloadKind"/>.</summary>
public sealed record ReplayPayloadCollections(IReadOnlyList<Llm1InstructionTrace> Inputs, IReadOnlyList<ForwardPropagationTrace> ForwardPasses, IReadOnlyList<Llm2EvaluationTrace> Evaluations, IReadOnlyList<LossTrace> Losses, IReadOnlyList<BackpropagationTrace> Backpropagations, IReadOnlyList<ParameterUpdateTrace> ParameterUpdates, IReadOnlyList<FinalVerdictTrace> FinalVerdicts, IReadOnlyList<SyntheticVoteGenerationTrace> VoteGeneration, IReadOnlyList<SyntheticVoteEvaluationTrace> VoteEvaluation, IReadOnlyList<SyntheticVoteSamplingTrace> VoteSampling);

/// <summary>
/// Root V2 replay document downloaded from <c>GET /api/neural-net/training/{id}/report</c>.
/// <see cref="SchemaVersion"/> must match the importer; non-finite floats are rejected/clamped at serialize time.
/// </summary>
public sealed record NeuralNetReplayReportV2(string SchemaVersion, Guid SessionId, ReplayCompletionStatus CompletionStatus, NeuralNetTopologySnapshot Topology, ReplayStringTable Strings, TrainingProvenance Provenance, NeuralNetParameterSnapshot InitialParameters, IReadOnlyList<TrainingTicketReplay> Tickets, IReadOnlyList<ReplayFrame> Frames, ReplayPayloadCollections Payloads, NeuralNetParameterSnapshot FinalParameters, ReplayIntegrity Integrity, ReplayFailure? Failure);

/// <summary>Ruleset outcome when promoting a session's final weights into a canonical checkpoint.</summary>
public sealed record PromotionValidationResult(bool Passed, IReadOnlyList<string> PassedRules, IReadOnlyList<string> FailedRules, string BaseChecksum, string FinalChecksum, int AppliedExampleCount, int CompletedEpochCount);

/// <summary>Promotion audit artifact linking a source session report to published canonical parameters.</summary>
public sealed record ModelPromotionReplay(Guid PromotionId, Guid SourceSessionId, string SourceReportChecksum, ReplayStringTable Strings, NeuralNetParameterSnapshot InitialParameters, IReadOnlyList<Guid> AppliedExampleIds, NeuralNetParameterSnapshot FinalParameters, PromotionValidationResult Validation, ReplayIntegrity Integrity);

/// <summary>Community-vote proposal emitted by the synthetic vote generator before sampling.</summary>
public sealed record SyntheticCommunityIntent(float ProposedApproval, int ProposedVoterCount, float Controversy, IReadOnlyList<string> Reasons);

/// <summary>LLM vote-generation step for synthetic community signals in tutoring/moderation drills.</summary>
public sealed record SyntheticVoteGenerationTrace(string ScenarioBucket, float GeneratorProposedApproval, int GeneratorProposedVoterCount, IReadOnlyList<string> GeneratorReasons, string GeneratorPromptVersion);

/// <summary>LLM critique of a generated vote proposal before sampling up/down counts.</summary>
public sealed record SyntheticVoteEvaluationTrace(float EvaluatorApprovalEstimate, float EvaluatorConfidence, IReadOnlyList<string> EvaluatorReasons, bool ProposalPlausible, float ProposalDifference, string EvaluatorPromptVersion);

/// <summary>Final sampled upvote/downvote counts feeding <see cref="ChatMonitoringNeuralModelInput.CommunityVote"/>.</summary>
public sealed record SyntheticVoteSamplingTrace(float FinalApprovalProbability, int VoterCount, int Upvotes, int Downvotes, string SamplingMethod, string SamplingPolicyVersion, string RandomAlgorithm, int RandomSeed);
