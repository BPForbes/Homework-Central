using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public interface INeuralNetTrainingService
{
    Task<IReadOnlyList<NeuralNetTrainingFeedbackDto>> GetPendingFeedbackAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingFeedbackDto> ApproveAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task RejectAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task<NeuralNetDataManagementDto> GetDataManagementAsync(CancellationToken ct = default);
    Task<NeuralNetVisualizerDto> GetVisualizerAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingSessionDto> StartSyntheticSessionAsync(StartNeuralNetTrainingRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(CancellationToken ct = default);
    Task<string?> GetSessionReportAsync(Guid sessionId, CancellationToken ct = default);
    Task RunSyntheticSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<bool> RunNextSyntheticSessionAsync(CancellationToken ct = default);
}

public sealed class NeuralNetTrainingService(
    AppDbContext db,
    ITicketStudentModel student,
    IVectorDocumentStore vectors,
    ILlmClient llm,
    INeuralNetTrainingQueue queue,
    SyntheticThreadScenarioGenerator scenarioGenerator,
    NeuralNetTrainingPromoter promoter) : INeuralNetTrainingService
{
    public async Task<IReadOnlyList<NeuralNetTrainingFeedbackDto>> GetPendingFeedbackAsync(CancellationToken ct = default)
    {
        List<TicketMessageScore> scores = await PendingQuery()
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct);
        Dictionary<Guid, string> messages = await LoadMessagesAsync(scores.Select(x => x.MessageId), ct);
        return scores.Select(score => Map(score, messages.GetValueOrDefault(score.MessageId))).ToList();
    }

    public async Task<NeuralNetTrainingFeedbackDto> ApproveAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default)
    {
        TicketMessageScore score = await db.TicketMessageScores
            .Include(x => x.Ticket).ThenInclude(x => x.Portal)
            .Include(x => x.Ticket).ThenInclude(x => x.Watches)
            .FirstOrDefaultAsync(x => x.ScoreEventId == scoreEventId, ct)
            ?? throw new InvalidOperationException("Training feedback was not found.");
        if (score.TrainingRejectedAtUtc is not null)
            throw new InvalidOperationException("Rejected feedback cannot be approved.");
        if (score.ReviewerScore is null || score.ReviewerRelevance is null)
            throw new InvalidOperationException("Only completed reviewer feedback can train the student.");

        string message = (await LoadMessagesAsync([score.MessageId], ct)).GetValueOrDefault(score.MessageId)
            ?? throw new InvalidOperationException("The original message is no longer available.");
        TicketUserWatch watch = score.Ticket.Watches.FirstOrDefault(x => x.TrackedUserId == score.TrackedUserId)
            ?? throw new InvalidOperationException("The score's tracking context is unavailable.");
        string requirement = TicketStudentContext.BuildRequirement(watch, 4000);
        string modelMessage = ComposeModelMessage(score.ContextSnapshot, message);
        TicketModelTrainingExample? training = await db.TicketModelTrainingExamples
            .FirstOrDefaultAsync(x => x.ScoreEventId == scoreEventId, ct);
        if (training is null)
        {
            DateTime now = DateTime.UtcNow;
            training = new TicketModelTrainingExample
            {
                TrainingExampleId = Guid.NewGuid(), MessageId = score.MessageId, ScoreEventId = score.ScoreEventId,
                Requirement = requirement, TargetScore = score.ReviewerScore.Value, TargetRelevance = score.ReviewerRelevance.Value,
                Category = score.StudentCategory, Source = "StaffApprovedReviewer", ApprovedAtUtc = now, ApprovedByUserId = actorUserId,
                ContextSnapshot = score.ContextSnapshot,
            };
            score.TrainingApprovedAtUtc = now;
            score.TrainingApprovedByUserId = actorUserId;
            db.TicketModelTrainingExamples.Add(training);
            await db.SaveChangesAsync(ct);
            student.Train(new(requirement, modelMessage, training.TargetScore, training.TargetRelevance, training.Category));
            await vectors.UpsertAsync(VectorNamespaces.TicketTrainingExample, message, student.Embed(message), training.Category,
                training.TrainingExampleId, new { training.TrainingExampleId, training.MessageId, training.ScoreEventId, training.Category, training.TargetScore, training.TargetRelevance, training.Source }, ct);
        }
        return Map(score, message);
    }

    public async Task RejectAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default)
    {
        TicketMessageScore score = await db.TicketMessageScores.FirstOrDefaultAsync(x => x.ScoreEventId == scoreEventId, ct)
            ?? throw new InvalidOperationException("Training feedback was not found.");
        if (score.TrainingApprovedAtUtc is not null)
            throw new InvalidOperationException("Approved feedback cannot be rejected.");
        if (score.TrainingRejectedAtUtc is null)
        {
            score.TrainingRejectedAtUtc = DateTime.UtcNow;
            score.TrainingRejectedByUserId = actorUserId;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<NeuralNetDataManagementDto> GetDataManagementAsync(CancellationToken ct = default)
    {
        List<TicketMessageScore> scores = await db.TicketMessageScores.AsNoTracking().ToListAsync(ct);
        int examples = await db.TicketModelTrainingExamples.CountAsync(ct);
        int vectors = await db.VectorDocuments.CountAsync(x => x.Namespace == VectorNamespaces.TicketTrainingExample, ct);
        return new NeuralNetDataManagementDto
        {
            PendingFeedback = scores.Count(x => x.ReviewerScore is not null && x.TrainingApprovedAtUtc is null && x.TrainingRejectedAtUtc is null),
            ApprovedFeedback = scores.Count(x => x.TrainingApprovedAtUtc is not null),
            RejectedFeedback = scores.Count(x => x.TrainingRejectedAtUtc is not null),
            TrainingExamples = examples, VectorExamples = vectors,
            CategoryCounts = scores.GroupBy(x => x.StudentCategory).ToDictionary(x => x.Key, x => x.Count()),
        };
    }

    public async Task<NeuralNetVisualizerDto> GetVisualizerAsync(CancellationToken ct = default) => new()
    {
        TrainingExamples = await db.TicketModelTrainingExamples.CountAsync(ct),
    };

    public async Task<NeuralNetTrainingSessionDto> StartSyntheticSessionAsync(
        StartNeuralNetTrainingRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        NeuralNetTrainingSession session = new()
        {
            SessionId = Guid.NewGuid(), StartedByUserId = actorUserId,
            RequestedTicketCount = Math.Clamp(request.TicketCount, 1, 10),
            MaxPassesPerTicket = Math.Clamp(request.MaxPassesPerTicket, 1, 6),
            Status = "Queued", CreatedAtUtc = DateTime.UtcNow,
        };
        db.NeuralNetTrainingSessions.Add(session);
        await db.SaveChangesAsync(ct);
        if (!queue.TryEnqueue(session.SessionId))
        {
            session.Status = "Failed";
            session.FailureReason = "The bounded synthetic-training queue is full. Try again shortly.";
            session.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return MapSession(session);
    }

    public async Task<IReadOnlyList<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(CancellationToken ct = default) =>
        (await db.NeuralNetTrainingSessions.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(50).ToListAsync(ct))
        .Select(MapSession).ToList();

    public async Task<string?> GetSessionReportAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.NeuralNetTrainingSessions.AsNoTracking().Where(x => x.SessionId == sessionId)
            .Select(x => x.ReportJson).FirstOrDefaultAsync(ct);

    public async Task<bool> RunNextSyntheticSessionAsync(CancellationToken ct = default)
    {
        Guid? sessionId = await db.NeuralNetTrainingSessions.AsNoTracking()
            .Where(x => x.Status == "Queued").OrderBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.SessionId).FirstOrDefaultAsync(ct);
        if (sessionId is null) return false;
        await RunSyntheticSessionAsync(sessionId.Value, ct);
        return true;
    }

    public async Task RunSyntheticSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        DateTime claimedAt = DateTime.UtcNow;
        int claimed = await db.NeuralNetTrainingSessions
            .Where(x => x.SessionId == sessionId && x.Status == "Queued")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Running")
                .SetProperty(x => x.StartedAtUtc, claimedAt), ct);
        if (claimed == 0) return;
        NeuralNetTrainingSession? session = await db.NeuralNetTrainingSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (session is null) return;
        ITicketStudentModelTelemetry telemetry = student as ITicketStudentModelTelemetry
            ?? throw new InvalidOperationException("The configured student model does not support replay telemetry.");
        ReplayBuilder replay = new(session, telemetry);
        SyntheticTrainingReport report = new(session.SessionId, claimedAt, session.RequestedTicketCount, session.MaxPassesPerTicket, student.GetStateSnapshot());
        try
        {
            for (int ticketIndex = 1; ticketIndex <= session.RequestedTicketCount; ticketIndex++)
            {
                SyntheticTicket? generated = await GenerateSyntheticTicketAsync(session.Mode, ct);
                if (generated is null)
                {
                    report.Tickets.Add(new(ticketIndex, null, [], "Generator returned invalid JSON."));
                    continue;
                }

                SyntheticTicketReport ticket = new(ticketIndex, generated, []);
                report.Tickets.Add(ticket);
                replay.BeginTicket(ticketIndex, generated);
                foreach (SyntheticThreadMessage message in generated.Messages)
                {
                    for (int pass = 1; pass <= session.MaxPassesPerTicket; pass++)
                    {
                        string requirement = $"{generated.Requirement}\nChannel: {message.Channel}\nAuthor role: {message.AuthorRole}";
                        string modelMessage = $"{ComposeModelMessage(generated.ContextSnapshot, message.Content)}\n<nn_metadata community_vote=0 channel_relevance={message.ChannelRelevance:F3} thread_position={message.MessageIndex / 8f:F3} prior_score=0>";
                        TicketStudentInferenceTrace initialInference = telemetry.PredictWithTrace(requirement, modelMessage);
                        TicketStudentPrediction prediction = initialInference.Prediction;
                        NeuralNetStateSnapshot before = student.GetStateSnapshot();
                        SyntheticEvaluatorResult? evaluation = await EvaluateSyntheticTicketAsync(generated with { Message = message.Content, Requirement = requirement }, prediction, ct);
                        if (evaluation is null)
                        {
                            ticket.Passes.Add(new(message.MessageIndex, pass, prediction, null, before, before, false, "Evaluator returned invalid JSON."));
                            break;
                        }

                        int seed = HashCode.Combine(session.SessionId, ticketIndex, message.MessageIndex, pass);
                        SyntheticCommunityResolution community = SyntheticCommunitySignalResolver.Resolve(
                            message.CommunityIntent,
                            (float)evaluation.ApprovalEstimate,
                            (float)evaluation.EvaluatorConfidence,
                            (float)evaluation.TargetScore,
                            message.ChannelRelevance,
                            seed);
                        evaluation = evaluation with { TargetScore = community.ResolvedEvidence };
                        bool lgtm = string.Equals(evaluation.Verdict, "LGTM", StringComparison.OrdinalIgnoreCase);
                        TrainingPassTrace? trainingTrace = null;
                        if (!lgtm)
                        {
                            float signedVote = community.Sampling.VoterCount == 0 ? 0 : ((float)community.Sampling.Upvotes - community.Sampling.Downvotes) / community.Sampling.VoterCount * community.VoteConfidence;
                            string trainingMessage = $"{ComposeModelMessage(generated.ContextSnapshot, message.Content)}\n<nn_metadata community_vote={signedVote:F3} channel_relevance={message.ChannelRelevance:F3} thread_position={message.MessageIndex / 8f:F3} prior_score={(float)prediction.EvidenceScore:F3}>";
                            StudentTrainingExample trainingExample = new(requirement, trainingMessage,
                                evaluation.TargetScore, evaluation.TargetRelevance, generated.Category);
                            trainingTrace = telemetry.TrainWithTrace(trainingExample, epochs: 12);
                            TicketModelTrainingExample record = new()
                            {
                                TrainingExampleId = Guid.NewGuid(), Requirement = requirement,
                                BootstrapMessage = trainingMessage, TargetScore = evaluation.TargetScore,
                                TargetRelevance = evaluation.TargetRelevance, Category = generated.Category,
                                Source = "SyntheticLlmTraining", ApprovedAtUtc = DateTime.UtcNow,
                                ApprovedByUserId = session.StartedByUserId,
                                NeuralNetTrainingSessionId = session.SessionId,
                                ContextSnapshot = generated.ContextSnapshot,
                            };
                            db.TicketModelTrainingExamples.Add(record);
                            await db.SaveChangesAsync(ct);
                            await vectors.UpsertAsync(VectorNamespaces.TicketTrainingExample, trainingMessage,
                                student.Embed(message.Content), generated.Category, record.TrainingExampleId,
                                new { record.TrainingExampleId, record.Category, record.TargetScore, record.TargetRelevance, record.Source }, ct);
                        }
                        NeuralNetStateSnapshot after = student.GetStateSnapshot();
                        ticket.Passes.Add(new(message.MessageIndex, pass, prediction, evaluation, before, after, lgtm, evaluation.Feedback));
                        replay.AddPass(ticketIndex, message, pass, generated, initialInference, evaluation, community, trainingTrace, lgtm);
                        if (lgtm) break;
                    }
                }
            }
            report.CompletedAtUtc = DateTime.UtcNow;
            report.FinalState = student.GetStateSnapshot();
            session.Status = "Completed";
            session.CompletedAtUtc = report.CompletedAtUtc;
            session.ReportJson = NeuralNetReplaySerializer.Serialize(replay.Build(ReplayCompletionStatus.Completed));
            await db.SaveChangesAsync(ct);
            await promoter.QueueSessionAsync(session.SessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.CompletedAtUtc = DateTime.UtcNow;
            report.FinalState = student.GetStateSnapshot();
            report.FailureReason = ex.Message;
            session.Status = "Failed";
            session.CompletedAtUtc = report.CompletedAtUtc;
            session.FailureReason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            session.ReportJson = NeuralNetReplaySerializer.Serialize(replay.Build(ReplayCompletionStatus.Failed, new("training", "unhandled", Truncate(ex.Message, 1000))));
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private IQueryable<TicketMessageScore> PendingQuery() => db.TicketMessageScores
        .AsNoTracking().Where(x => x.ReviewerScore != null && x.TrainingApprovedAtUtc == null && x.TrainingRejectedAtUtc == null);

    private async Task<Dictionary<Guid, string>> LoadMessagesAsync(IEnumerable<Guid> messageIds, CancellationToken ct)
    {
        Guid[] ids = messageIds.Distinct().ToArray();
        return await db.ChatMessages.AsNoTracking().Where(x => ids.Contains(x.MessageId))
            .ToDictionaryAsync(x => x.MessageId, x => x.RawContent, ct);
    }

    private static NeuralNetTrainingFeedbackDto Map(TicketMessageScore score, string? message) => new()
    {
        ScoreEventId = score.ScoreEventId, TicketId = score.TicketId, MessageId = score.MessageId,
        MessagePreview = Truncate(message ?? "Message unavailable", 500), Category = score.StudentCategory,
        StudentScore = score.StudentScore, StudentConfidence = score.StudentConfidence,
        ReviewerScore = score.ReviewerScore ?? 0, ReviewerConfidence = score.ReviewerConfidence ?? 0,
        CorrectionNeeded = score.CorrectionNeeded, Explanation = score.ReviewerExplanation,
        Guidance = score.ReviewerGuidance, CreatedAtUtc = score.CreatedAtUtc,
    };

    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
    private static string ComposeModelMessage(string? contextSnapshot, string message) =>
        string.IsNullOrWhiteSpace(contextSnapshot) ? message : $"{contextSnapshot}\n<current_message>\n{message}\n</current_message>";

    private async Task<SyntheticTicket?> GenerateSyntheticTicketAsync(NeuralTrainingMode mode, CancellationToken ct)
    {
        SyntheticThreadScenario? scenario = await scenarioGenerator.GenerateAsync(mode, ct);
        SyntheticThreadMessage? primaryMessage = scenario?.Messages.FirstOrDefault(x => !x.IsDistractor)
            ?? scenario?.Messages.FirstOrDefault();
        if (scenario is not null && primaryMessage is not null)
        {
            string requirement = $"{scenario.Requirement}\nChannel: {primaryMessage.Channel}\nAuthor role: {primaryMessage.AuthorRole}";
            return new SyntheticTicket(
                scenario.Category,
                requirement,
                primaryMessage.Content,
                scenario.InitialContext,
                .5,
                primaryMessage.ChannelRelevance,
                scenario.Messages);
        }

        const string systemPrompt = "Generate short fictional moderation-ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Scores are 0 to 1. Never include real personal data.";
        string? response = await llm.ChatJsonAsync(systemPrompt, "Create one varied school-chat moderation example.", ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            string category = GetString(root, "category"), requirement = GetString(root, "requirement"), message = GetString(root, "message");
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(requirement) || string.IsNullOrWhiteSpace(message)) return null;
            SyntheticThreadMessage fallbackMessage = new(0, "synthetic-user", "student", "general", message[..Math.Min(4000, message.Length)], false, (float)GetUnit(root, "expectedRelevance", .5), new(.5f, 10, .5f, []));
            return new(category[..Math.Min(80, category.Length)], requirement[..Math.Min(4000, requirement.Length)], message[..Math.Min(4000, message.Length)], Truncate(GetString(root, "contextSnapshot"), 2500), GetUnit(root, "expectedScore", .5), GetUnit(root, "expectedRelevance", .5), [fallbackMessage]);
        }
        catch (JsonException) { return null; }
    }

    private async Task<SyntheticEvaluatorResult?> EvaluateSyntheticTicketAsync(SyntheticTicket ticket, TicketStudentPrediction prediction, CancellationToken ct)
    {
        const string systemPrompt = "You are an independent evaluator for a small school-chat classifier. Return JSON only: verdict (LGTM or REVISE), targetScore (0..1), targetRelevance (0..1), approvalEstimate (0..1), evaluatorConfidence (0..1), feedback. You receive no proposed vote data. Use LGTM only if the student classification is sufficiently correct.";
        string prompt = $"<requirement>{ticket.Requirement}</requirement>\n<context>{ticket.ContextSnapshot}</context>\n<message>{ticket.Message}</message>\n<student_score>{prediction.EvidenceScore:F3}</student_score>\n<student_relevance>{prediction.Relevance:F3}</student_relevance>\n<student_confidence>{prediction.Confidence:F3}</student_confidence>";
        string? response = await llm.ChatJsonAsync(systemPrompt, prompt, ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            string verdict = GetString(root, "verdict");
            if (!string.Equals(verdict, "LGTM", StringComparison.OrdinalIgnoreCase) && !string.Equals(verdict, "REVISE", StringComparison.OrdinalIgnoreCase)) return null;
            return new(
                verdict.ToUpperInvariant(),
                GetUnit(root, "targetScore", prediction.EvidenceScore),
                GetUnit(root, "targetRelevance", prediction.Relevance),
                Truncate(GetString(root, "feedback"), 2000),
                GetUnit(root, "approvalEstimate", .5),
                GetUnit(root, "evaluatorConfidence", prediction.Confidence));
        }
        catch (JsonException) { return null; }
    }

    private static string GetString(JsonElement root, string property) => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double GetUnit(JsonElement root, string property, double fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? Math.Clamp(result, 0, 1) : fallback;
    private static NeuralNetTrainingSessionDto MapSession(NeuralNetTrainingSession session) => new()
    {
        SessionId = session.SessionId, RequestedTicketCount = session.RequestedTicketCount, MaxPassesPerTicket = session.MaxPassesPerTicket, Mode = session.Mode, Status = session.Status,
        CreatedAtUtc = session.CreatedAtUtc, StartedAtUtc = session.StartedAtUtc, CompletedAtUtc = session.CompletedAtUtc, FailureReason = session.FailureReason, HasReport = !string.IsNullOrWhiteSpace(session.ReportJson),
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record SyntheticTicket(string Category, string Requirement, string Message, string ContextSnapshot, double ExpectedScore, double ExpectedRelevance, IReadOnlyList<SyntheticThreadMessage> Messages);
    private sealed record SyntheticEvaluatorResult(string Verdict, double TargetScore, double TargetRelevance, string Feedback, double ApprovalEstimate, double EvaluatorConfidence);
    private sealed record SyntheticPassReport(int MessageIndex, int Pass, TicketStudentPrediction Prediction, SyntheticEvaluatorResult? Evaluation, NeuralNetStateSnapshot BeforeState, NeuralNetStateSnapshot AfterState, bool Lgtm, string Feedback);
    private sealed record SyntheticTicketReport(int TicketIndex, SyntheticTicket? Ticket, List<SyntheticPassReport> Passes, string? GeneratorFailure = null);
    private sealed record SyntheticTrainingReport(Guid SessionId, DateTime StartedAtUtc, int RequestedTicketCount, int MaxPassesPerTicket, NeuralNetStateSnapshot InitialState)
    {
        public List<SyntheticTicketReport> Tickets { get; } = [];
        public DateTime? CompletedAtUtc { get; set; }
        public NeuralNetStateSnapshot? FinalState { get; set; }
        public string? FailureReason { get; set; }
    }

    private sealed class ReplayBuilder
    {
        private readonly NeuralNetTrainingSession session;
        private readonly ITicketStudentModelTelemetry telemetry;
        private readonly Dictionary<string, int> stringIndices = new(StringComparer.Ordinal);
        private readonly List<string> strings = [];
        private readonly List<TicketState> tickets = [];
        private readonly List<ReplayFrame> frames = [];
        private readonly List<Llm1InstructionTrace> inputs = [];
        private readonly List<ForwardPropagationTrace> forwards = [];
        private readonly List<Llm2EvaluationTrace> evaluations = [];
        private readonly List<LossTrace> losses = [];
        private readonly List<BackpropagationTrace> backwards = [];
        private readonly List<ParameterUpdateTrace> updates = [];
        private readonly List<FinalVerdictTrace> verdicts = [];
        private readonly List<SyntheticVoteGenerationTrace> voteGeneration = [];
        private readonly List<SyntheticVoteEvaluationTrace> voteEvaluation = [];
        private readonly List<SyntheticVoteSamplingTrace> voteSampling = [];
        private readonly NeuralNetParameterSnapshot initial;
        private long sequence;
        private int localRevision;

        public ReplayBuilder(NeuralNetTrainingSession session, ITicketStudentModelTelemetry telemetry)
        {
            this.session = session; this.telemetry = telemetry;
            initial = telemetry.GetParameterSnapshot(0, 0);
        }

        public void BeginTicket(int ticketIndex, SyntheticTicket ticket) => tickets.Add(new TicketState(ticketIndex, ticket, []));

        public void AddPass(int ticketIndex, SyntheticThreadMessage message, int passIndex, SyntheticTicket ticket,
            TicketStudentInferenceTrace initialInference, SyntheticEvaluatorResult evaluation,
            SyntheticCommunityResolution community, TrainingPassTrace? training, bool accepted)
        {
            TicketState ticketState = tickets.Single(x => x.Index == ticketIndex);
            MessageState messageState = ticketState.Messages.FirstOrDefault(x => x.Message.MessageIndex == message.MessageIndex)
                ?? new MessageState(message, []);
            if (!ticketState.Messages.Contains(messageState)) ticketState.Messages.Add(messageState);

            int inputIndex = inputs.Count;
            inputs.Add(new(Intern(ticket.Requirement), Intern(ticket.ContextSnapshot), Intern(message.Content), Intern(message.Channel), Intern(message.AuthorRole), message.IsDistractor, message.ChannelRelevance, "llm-1", "synthetic-thread-v1"));
            Frame(ReplayPhase.Llm1Input, ReplayPayloadKind.Llm1Input, ticketIndex, passIndex, message.MessageIndex, null, inputIndex);
            int initialForward = AddForward(ReplayPhase.InitialForward, ticketIndex, passIndex, message.MessageIndex, null, initialInference.Forward);

            int evaluationIndex = evaluations.Count;
            evaluations.Add(new(true, accepted, accepted, (float)evaluation.TargetScore, (float)evaluation.TargetRelevance,
                (float)evaluation.TargetScore, (float)evaluation.ApprovalEstimate, (float)evaluation.EvaluatorConfidence, [], Intern(evaluation.Feedback), "llm-2", "blind-evaluator-v1"));
            Frame(ReplayPhase.Llm2Evaluation, ReplayPayloadKind.Evaluation, ticketIndex, passIndex, message.MessageIndex, null, evaluationIndex);
            int generationIndex = voteGeneration.Count; voteGeneration.Add(new("balanced", message.CommunityIntent.ProposedApproval, message.CommunityIntent.ProposedVoterCount, message.CommunityIntent.Reasons, "synthetic-thread-v1"));
            int voteEvaluationIndex = voteEvaluation.Count; voteEvaluation.Add(community.Evaluation);
            int samplingIndex = voteSampling.Count; voteSampling.Add(community.Sampling);
            Frame(ReplayPhase.VoteResolution, ReplayPayloadKind.VoteSampling, ticketIndex, passIndex, message.MessageIndex, null, samplingIndex);

            List<TrainingIterationReplay> iterations = training?.Iterations.ToList() ?? [];
            foreach (TrainingIterationReplay iteration in iterations)
            {
                AddForward(ReplayPhase.EpochForward, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, iteration.BeforeUpdate);
                int lossBefore = losses.Count; losses.Add(iteration.LossBeforeUpdate); Frame(ReplayPhase.LossCalculation, ReplayPayloadKind.Loss, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, lossBefore);
                int back = backwards.Count; backwards.Add(iteration.Backward); Frame(ReplayPhase.BackwardPropagation, ReplayPayloadKind.Backpropagation, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, back);
                int update = updates.Count; updates.Add(iteration.Update); Frame(ReplayPhase.ParameterUpdate, ReplayPayloadKind.ParameterUpdate, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, update);
                AddForward(ReplayPhase.PostUpdateForward, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, iteration.AfterUpdate);
                int lossAfter = losses.Count; losses.Add(iteration.LossAfterUpdate); Frame(ReplayPhase.LossCalculation, ReplayPayloadKind.Loss, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, lossAfter);
                localRevision++;
            }
            int? finalForward = iterations.Count == 0 ? null : forwards.Count - 1;
            int verdict = verdicts.Count; verdicts.Add(new(accepted, Intern(accepted ? "LLM 2 accepted the prediction." : evaluation.Feedback), (float)evaluation.TargetScore, .75f, iterations.Count, initialForward, finalForward));
            Frame(ReplayPhase.FinalVerdict, ReplayPayloadKind.FinalVerdict, ticketIndex, passIndex, message.MessageIndex, null, verdict);
            messageState.Passes.Add(new(passIndex, message.MessageIndex, inputIndex, initialForward, evaluationIndex, generationIndex, voteEvaluationIndex, samplingIndex, iterations, finalForward, telemetry.GetParameterSnapshot(null, localRevision)));
        }

        public NeuralNetReplayReportV2 Build(ReplayCompletionStatus status, ReplayFailure? failure = null)
        {
            NeuralNetParameterSnapshot final = telemetry.GetParameterSnapshot(null, localRevision);
            IReadOnlyList<TrainingTicketReplay> ticketReplay = tickets.Select(ticket => new TrainingTicketReplay(ticket.Index, Intern(ticket.Ticket.Category), Intern(ticket.Ticket.Requirement), Intern(ticket.Ticket.ContextSnapshot), ticket.Messages.Select(message => new TrainingMessageReplay(message.Message.MessageIndex, Intern(message.Message.AuthorId), Intern(message.Message.AuthorRole), Intern(message.Message.Channel), message.Message.IsDistractor, message.Message.ChannelRelevance, message.Passes)).ToList())).ToList();
            ReplayPayloadCollections payloads = new(inputs, forwards, evaluations, losses, backwards, updates, verdicts, voteGeneration, voteEvaluation, voteSampling);
            TrainingProvenance provenance = new("hc-student-mlp-v2", "hashed-text-256-v1", "binary-cross-entropy-v1", "SGD", .035f, 12, "hc-xoshiro256ss-v1", 0x48434D4C, "replay-v2-worker-v1");
            ReplayIntegrity placeholder = new("hc-replay-canonical-json-v1", "sha-256", "", initial.Checksum, final.Checksum, "");
            NeuralNetReplayReportV2 draft = new("2.0", session.SessionId, status, telemetry.GetTopologySnapshot(), new(strings), provenance, initial, ticketReplay, frames, payloads, final, placeholder, failure);
            ReplayIntegrity integrity = NeuralNetReplaySerializer.CreateIntegrity(draft.Topology, initial, final, NeuralNetReplaySerializer.Serialize(draft));
            NeuralNetReplayReportV2 result = draft with { Integrity = integrity };
            NeuralNetReplaySerializer.Validate(result);
            return result;
        }

        private int Intern(string value) { if (stringIndices.TryGetValue(value, out int existing)) return existing; int index = strings.Count; strings.Add(value); stringIndices[value] = index; return index; }
        private int AddForward(ReplayPhase phase, int ticket, int pass, int message, int? epoch, ForwardPropagationTrace forward) { int index = forwards.Count; forwards.Add(forward); Frame(phase, ReplayPayloadKind.Forward, ticket, pass, message, epoch, index); return index; }
        private void Frame(ReplayPhase phase, ReplayPayloadKind kind, int ticket, int pass, int? message, int? epoch, int payload) => frames.Add(new(++sequence, phase, kind, ticket, pass, message, epoch, DateTimeOffset.UtcNow, payload));
        private sealed record TicketState(int Index, SyntheticTicket Ticket, List<MessageState> Messages);
        private sealed record MessageState(SyntheticThreadMessage Message, List<TrainingPassReplay> Passes);
    }
}
