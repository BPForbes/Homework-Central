using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public enum NeuralNetTrainingSessionRemovalResult
{
    Removed,
    NotFound,
    Running,
}

public interface INeuralNetTrainingService
{
    Task<IReadOnlyList<NeuralNetTrainingFeedbackDto>> GetPendingFeedbackAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingFeedbackDto> ApproveAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task RejectAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task<NeuralNetDataManagementDto> GetDataManagementAsync(CancellationToken ct = default);
    Task<NeuralNetVisualizerDto> GetVisualizerAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingSessionDto> StartSyntheticSessionAsync(StartNeuralNetTrainingRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(CancellationToken ct = default);
    Task<string?> GetSessionReportAsync(Guid sessionId, NeuralModelKindChatMonitoring? chatMonitoringKind = null, CancellationToken ct = default);
    Task RunSyntheticSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<bool> RunNextSyntheticSessionAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingSessionRemovalResult> RemoveSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class NeuralNetTrainingService(
    AppDbContext db,
    IChatMonitoringNeuralModelFactory chatMonitoringModels,
    IVectorDocumentStore vectors,
    ILlmClient llm,
    INeuralNetTrainingQueue queue,
    SyntheticThreadScenarioGenerator scenarioGenerator,
    NeuralNetTrainingPromoter promoter,
    Microsoft.Extensions.Options.IOptions<NeuralNetTrainingOptions> trainingOptions,
    Microsoft.Extensions.Logging.ILogger<NeuralNetTrainingService> logger) : INeuralNetTrainingService
{
    private NeuralNetTrainingOptions Options => trainingOptions.Value;
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
        string requirement = ChatMonitoringTicketContext.BuildRequirement(watch, 4000);
        string modelMessage = ComposeModelMessage(score.ContextSnapshot, message);
        NeuralModelKindChatMonitoring chatMonitoringKind = ChatMonitoringTicketContext.ResolveKind(watch);
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
                ChatMonitoringKind = chatMonitoringKind,
            };
            score.TrainingApprovedAtUtc = now;
            score.TrainingApprovedByUserId = actorUserId;
            db.TicketModelTrainingExamples.Add(training);
            await db.SaveChangesAsync(ct);
            IChatMonitoringNeuralModel model = chatMonitoringModels.Get(chatMonitoringKind);
            model.Train(new ChatMonitoringNeuralModelInput(requirement, score.ContextSnapshot ?? string.Empty, message, 0, 1, 0, .5f),
                new ChatMonitoringNeuralModelTargets((float)training.TargetScore, (float)training.TargetRelevance));
            await vectors.UpsertAsync(VectorNamespaces.TicketTrainingExample, message, ChatMonitoringFeatureEncoder.EmbedText(message),
                ChatMonitoringVectorKeys.LineagePositionId(chatMonitoringKind),
                training.TrainingExampleId, new { training.TrainingExampleId, training.MessageId, training.ScoreEventId, training.Category, training.TargetScore, training.TargetRelevance, training.Source, chatMonitoringKind }, ct);
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

    public async Task<NeuralNetVisualizerDto> GetVisualizerAsync(CancellationToken ct = default)
    {
        int trainingExamples = await db.TicketModelTrainingExamples.CountAsync(ct);
        List<NeuralNetVisualizerModelDto> models = chatMonitoringModels.Resolve(NeuralTrainingMode.Both)
            .Select(model =>
            {
                ChatMonitoringNeuralModelStateSnapshot state = ((IChatMonitoringNeuralModelTelemetry)model).GetStateSnapshot();
                NeuralNetTopologySnapshot topology = ((IChatMonitoringNeuralModelTelemetry)model).GetTopologySnapshot();
                bool tutoring = state.ChatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring;
                return new NeuralNetVisualizerModelDto
                {
                    ChatMonitoringKind = state.ChatMonitoringKind,
                    ModelVersion = state.ModelVersion,
                    LayerWidths = state.LayerWidths,
                    LayerLabels = state.LayerLabels,
                    ParameterCount = state.ParameterCount,
                    SupportExamples = state.SupportExamples,
                    NodeCount = topology.Nodes.Count,
                    Stage1LayerWidths = tutoring
                        ? [TutoringSubjectContextRouter.InputSize, TutoringSubjectContextRouter.HiddenSize, TutoringSubjectContextRouter.OutputSize]
                        : [ModerationConceptContextRouter.InputSize, 24, ModerationConceptContextRouter.OutputSize],
                    Stage1Role = tutoring ? "subject-context router" : "concept-context router",
                    CategoryCount = tutoring
                        ? ChatMonitoringCategoryTaxonomy.Tutoring.Length
                        : ChatMonitoringCategoryTaxonomy.Moderation.Length,
                    CascadeComposition = "g(f(x))",
                    ChainRuleSummary = "∂C/∂θ_f = (∂C/∂f)(∂f/∂θ_f)",
                    RuntimeKind = ChatMonitoringNeuralModelHashedMlp.RuntimeKind,
                };
            }).ToList();
        NeuralNetVisualizerModelDto primary = models[0];
        return new NeuralNetVisualizerDto
        {
            Models = models,
            TrainingExamples = trainingExamples,
            InputNodes = primary.LayerWidths[0],
            HiddenNodes = primary.LayerWidths.Skip(1).Take(primary.LayerWidths.Count - 2).Sum(),
            ModelVersion = primary.ModelVersion,
        };
    }

    public async Task<NeuralNetTrainingSessionDto> StartSyntheticSessionAsync(
        StartNeuralNetTrainingRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        NeuralNetTrainingSession session = new()
        {
            SessionId = Guid.NewGuid(), StartedByUserId = actorUserId,
            RequestedTicketCount = Math.Clamp(request.TicketCount, 1, 10),
            MaxPassesPerTicket = Math.Clamp(request.MaxPassesPerTicket, 1, 6),
            Mode = request.Mode,
            Status = "Queued", CreatedAtUtc = DateTime.UtcNow,
        };
        db.NeuralNetTrainingSessions.Add(session);
        foreach (NeuralModelKindChatMonitoring chatMonitoringKind in GetChatMonitoringKinds(request.Mode))
        {
            db.ChatMonitoringNeuralModelRuns.Add(new ChatMonitoringNeuralModelRun
            {
                RunId = Guid.NewGuid(),
                SessionId = session.SessionId,
                ChatMonitoringKind = chatMonitoringKind,
                Status = "Queued",
                CreatedAtUtc = session.CreatedAtUtc,
            });
        }
        await db.SaveChangesAsync(ct);
        if (!queue.TryEnqueue(session.SessionId))
        {
            session.Status = "Failed";
            session.FailureReason = "The bounded synthetic-training queue is full. Try again shortly.";
            session.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            // A concurrent removal can land between the row being saved and it being enqueued above,
            // finding nothing yet to pull from the queue. Re-checking here and undoing the enqueue if
            // the row is already gone closes that race instead of leaving a stale ID occupying a slot.
            bool stillQueued = await db.NeuralNetTrainingSessions.AsNoTracking()
                .AnyAsync(x => x.SessionId == session.SessionId && x.Status == "Queued", ct);
            if (!stillQueued) queue.TryRemove(session.SessionId);
        }
        return MapSession(session);
    }

    public async Task<IReadOnlyList<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(CancellationToken ct = default)
    {
        List<NeuralNetTrainingSession> sessions = await db.NeuralNetTrainingSessions.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(50).ToListAsync(ct);
        Guid[] sessionIds = sessions.Select(x => x.SessionId).ToArray();
        List<ChatMonitoringNeuralModelRun> runs = await db.ChatMonitoringNeuralModelRuns.AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId)).ToListAsync(ct);
        return sessions.Select(session => MapSession(session, runs.Where(run => run.SessionId == session.SessionId))).ToList();
    }

    public async Task<NeuralNetTrainingSessionRemovalResult> RemoveSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        // A single conditional DELETE is atomic against the worker's own claim UPDATE: whichever
        // statement the database serializes first wins the row, so a session can never be removed
        // out from under a run that has just started.
        int removed = await db.NeuralNetTrainingSessions
            .Where(x => x.SessionId == sessionId && x.Status != "Running")
            .ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            bool exists = await db.NeuralNetTrainingSessions.AsNoTracking().AnyAsync(x => x.SessionId == sessionId, ct);
            await transaction.RollbackAsync(ct);
            return exists ? NeuralNetTrainingSessionRemovalResult.Running : NeuralNetTrainingSessionRemovalResult.NotFound;
        }
        await db.ChatMonitoringNeuralModelRuns.Where(x => x.SessionId == sessionId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        // If the session was still waiting (not yet claimed by the worker), this frees its slot in the
        // bounded queue immediately instead of leaving a stale ID that blocks new requests until drained.
        queue.TryRemove(sessionId);
        return NeuralNetTrainingSessionRemovalResult.Removed;
    }

    public async Task<string?> GetSessionReportAsync(Guid sessionId, NeuralModelKindChatMonitoring? chatMonitoringKind = null, CancellationToken ct = default)
    {
        if (chatMonitoringKind is not null)
        {
            return await db.ChatMonitoringNeuralModelRuns.AsNoTracking()
                .Where(x => x.SessionId == sessionId && x.ChatMonitoringKind == chatMonitoringKind.Value)
                .Select(x => x.WorkerReplayJson).FirstOrDefaultAsync(ct);
        }

        return await db.NeuralNetTrainingSessions.AsNoTracking().Where(x => x.SessionId == sessionId)
            .Select(x => x.ReportJson).FirstOrDefaultAsync(ct);
    }

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
        TrainingSessionTimings timings = new();
        try
        {
            // Scenario generation is pure LLM/IO work with no shared DbContext or model state, so the
            // tickets are generated concurrently instead of one awaited call at a time.
            System.Diagnostics.Stopwatch llm1Watch = System.Diagnostics.Stopwatch.StartNew();
            Task<SyntheticTicket?>[] generationTasks = Enumerable.Range(1, session.RequestedTicketCount)
                .Select(_ => GenerateSyntheticTicketAsync(session.Mode, timings, ct)).ToArray();
            SyntheticTicket?[] generated = await Task.WhenAll(generationTasks);
            llm1Watch.Stop();
            timings.Llm1ScenarioMs += llm1Watch.ElapsedMilliseconds;
            List<(int TicketIndex, SyntheticTicket? Ticket)> tickets = generated
                .Select((ticket, index) => (TicketIndex: index + 1, Ticket: ticket)).ToList();

            List<ChatMonitoringNeuralModelRun> runs = await db.ChatMonitoringNeuralModelRuns
                .Where(x => x.SessionId == session.SessionId).OrderBy(x => x.ChatMonitoringKind).ToListAsync(ct);
            using SemaphoreSlim persistenceGate = new(1, 1);
            List<Task> runTasks = runs.Select(run => RunChatMonitoringModelAsync(session, run, tickets, persistenceGate, timings, ct)).ToList();
            await Task.WhenAll(runTasks);
            session.Status = "Completed";
            session.CompletedAtUtc = DateTime.UtcNow;
            session.ReportJson = SerializeTrainingReport(timings);
            logger.LogInformation(
                "Synthetic training session {SessionId} completed. LLM1={Llm1}ms labels={Labels}ms audits={Audits}ms train={Train}ms db={Db}ms vectors={Vectors}ms examples={Examples} audits={AuditCount}",
                session.SessionId, timings.Llm1ScenarioMs, timings.TeacherLabelMs, timings.AuditMs, timings.TrainMs, timings.DbSaveMs, timings.VectorUpsertMs, timings.ExamplesPersisted, timings.AuditCount);
            await db.SaveChangesAsync(ct);
            await promoter.QueueSessionAsync(session.SessionId, ct);
        }
        catch (OperationCanceledException)
        {
            session.Status = "Cancelled";
            session.CompletedAtUtc = DateTime.UtcNow;
            session.FailureReason = "Training cancelled.";
            session.ReportJson = SerializeTrainingReport(timings);
            List<ChatMonitoringNeuralModelRun> runningRuns = await db.ChatMonitoringNeuralModelRuns
                .Where(x => x.SessionId == session.SessionId && x.Status == "Running")
                .ToListAsync(CancellationToken.None);
            foreach (ChatMonitoringNeuralModelRun run in runningRuns)
            {
                run.Status = "Cancelled";
                run.CompletedAtUtc = DateTime.UtcNow;
                run.FailureReason ??= "Training cancelled.";
            }

            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            session.Status = "Failed";
            session.CompletedAtUtc = DateTime.UtcNow;
            session.FailureReason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            session.ReportJson = SerializeTrainingReport(timings);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private string SerializeTrainingReport(TrainingSessionTimings timings)
    {
        try
        {
            return JsonSerializer.Serialize(timings.ToReport(), JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize neural-net training report.");
            return """{"error":"report-serialization-failed"}""";
        }
    }

    private async Task RunChatMonitoringModelAsync(
        NeuralNetTrainingSession session,
        ChatMonitoringNeuralModelRun run,
        IReadOnlyList<(int TicketIndex, SyntheticTicket? Ticket)> tickets,
        SemaphoreSlim persistenceGate,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        IChatMonitoringNeuralModelTelemetry telemetry = chatMonitoringModels.Get(run.ChatMonitoringKind) as IChatMonitoringNeuralModelTelemetry
            ?? throw new InvalidOperationException("The configured chat-monitoring model does not support replay telemetry.");
        ReplayBuilder replay = new(session, telemetry);
        run.Status = "Running";
        run.StartedAtUtc = DateTime.UtcNow;
        await PersistAsync(persistenceGate, timings, ct);
        PersistenceBatch batch = new(db, vectors, persistenceGate, Options.PersistenceBatchSize, timings);
        List<PendingTrainItem> pendingTrain = [];
        try
        {
            IReadOnlyList<(int TicketIndex, SyntheticTicket Ticket)> selected = SelectTicketsForRun(session.SessionId, run.ChatMonitoringKind, tickets, Options.CrossDomainSampleRate);
            int miniBatchSize = Math.Clamp(Options.MiniBatchSize, 1, 64);
            foreach ((int ticketIndex, SyntheticTicket generated) in selected)
            {
                replay.BeginTicket(ticketIndex, generated);
                foreach (SyntheticThreadMessage message in generated.Messages)
                {
                    string requirement = $"{generated.Requirement}\nChannel: {message.Channel}\nAuthor role: {message.AuthorRole}";
                    SubjectSignalSnapshot subjectSignals = run.ChatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring
                        ? ChatMonitoringSubjectSignals.ResolveFromSynthetic(generated.Category, generated.Requirement, message.Channel, message.ChannelRelevance)
                        : ChatMonitoringSubjectSignals.Resolve([], ChatMonitoringSubjectSignals.ResolveChannelSubject(message.Channel), message.ChannelRelevance);
                    ChatMonitoringNeuralModelInput input = ChatMonitoringNeuralModelInput.Create(
                        requirement,
                        generated.ContextSnapshot,
                        message.Content,
                        communityVote: 0,
                        threadContinuity: message.MessageIndex / 8f,
                        priorScore: 0,
                        subjectSignals);
                    ChatMonitoringNeuralModelInferenceTrace initialInference = telemetry.PredictWithTrace(input);
                    ChatMonitoringNeuralModelPrediction prediction = initialInference.Prediction;

                    SyntheticEvaluatorResult evaluation = ResolveTeacherEvaluation(generated, message, prediction, run.ChatMonitoringKind);
                    int seed = HashCode.Combine(session.SessionId, ticketIndex, message.MessageIndex, 1, (int)run.ChatMonitoringKind);
                    SyntheticCommunityResolution community = SyntheticCommunitySignalResolver.Resolve(message.CommunityIntent,
                        (float)evaluation.ApprovalEstimate, (float)evaluation.EvaluatorConfidence, (float)evaluation.TargetScore, subjectSignals.EffectiveChannelRelevance, seed);
                    evaluation = evaluation with { TargetScore = community.ResolvedEvidence };
                    if (run.ChatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring)
                        evaluation = evaluation with { TargetRelevance = Math.Clamp(evaluation.TargetRelevance * subjectSignals.RewardScale, 0, 1) };

                    bool withinTolerance = Math.Abs(prediction.Evidence - evaluation.TargetScore) <= Options.EvidenceTolerance
                        && Math.Abs(prediction.Relevance - evaluation.TargetRelevance) <= Options.RelevanceTolerance;

                    if (withinTolerance)
                    {
                        if (ShouldAudit(session.SessionId, ticketIndex, message.MessageIndex, run.ChatMonitoringKind))
                            evaluation = await MaybeAuditAsync(generated, message, requirement, prediction, evaluation, timings, ct);
                        replay.AddPass(ticketIndex, message, 1, generated, initialInference, evaluation, community, null, true);
                        continue;
                    }

                    float signedVote = community.Sampling.VoterCount == 0 ? 0 : ((float)community.Sampling.Upvotes - community.Sampling.Downvotes) / community.Sampling.VoterCount * community.VoteConfidence;
                    ChatMonitoringNeuralModelInput trainingInput = input with { CommunityVote = signedVote, PriorScore = prediction.Evidence };
                    int categoryIndex = ChatMonitoringTicketContext.CategoryIndex(generated.Category, run.ChatMonitoringKind);
                    ChatMonitoringNeuralModelTrainingExample trainingExample = new(trainingInput,
                        new ChatMonitoringNeuralModelTargets((float)evaluation.TargetScore, (float)evaluation.TargetRelevance, categoryIndex), generated.Category);
                    pendingTrain.Add(new PendingTrainItem(ticketIndex, message, generated, requirement, trainingInput, trainingExample, initialInference, evaluation, community,
                        ShouldCaptureFullTrace(session.SessionId, ticketIndex, message.MessageIndex, run.ChatMonitoringKind)));

                    if (pendingTrain.Count >= miniBatchSize)
                        await FlushTrainMiniBatchAsync(session, run, telemetry, replay, batch, pendingTrain, timings, ct);
                }
            }

            if (pendingTrain.Count > 0)
                await FlushTrainMiniBatchAsync(session, run, telemetry, replay, batch, pendingTrain, timings, ct);

            await batch.FlushAsync(ct);
            run.Status = "Completed";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.WorkerReplayJson = NeuralNetReplaySerializer.Serialize(replay.Build(ReplayCompletionStatus.Completed, epochs: Options.LocalEpochs));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (pendingTrain.Count > 0)
            {
                try { await FlushTrainMiniBatchAsync(session, run, telemetry, replay, batch, pendingTrain, timings, CancellationToken.None); }
                catch { /* best-effort drain */ }
            }

            try { await batch.FlushAsync(CancellationToken.None); }
            catch { /* best-effort drain; still record failed run below */ }

            run.Status = "Failed";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.FailureReason = Truncate(ex.Message, 1000);
            run.WorkerReplayJson = NeuralNetReplaySerializer.Serialize(replay.Build(ReplayCompletionStatus.Failed, new("training", "unhandled", Truncate(ex.Message, 1000)), Options.LocalEpochs));
            throw;
        }
        finally { await PersistAsync(persistenceGate, timings, CancellationToken.None); }
    }

    private async Task FlushTrainMiniBatchAsync(
        NeuralNetTrainingSession session,
        ChatMonitoringNeuralModelRun run,
        IChatMonitoringNeuralModelTelemetry telemetry,
        ReplayBuilder replay,
        PersistenceBatch batch,
        List<PendingTrainItem> pending,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        if (pending.Count == 0) return;
        List<PendingTrainItem> items = [.. pending];
        pending.Clear();

        int localEpochs = Math.Clamp(Options.LocalEpochs, 1, 100);
        if (session.MaxPassesPerTicket > 1)
            localEpochs = Math.Clamp(localEpochs * session.MaxPassesPerTicket / 3, 12, 100);
        NeuralTrainingTraceDetail detail = items.Any(x => x.FullTrace)
            ? NeuralTrainingTraceDetail.Full
            : NeuralTrainingTraceDetail.Compact;

        System.Diagnostics.Stopwatch trainWatch = System.Diagnostics.Stopwatch.StartNew();
        TrainingPassTrace trainingTrace = telemetry.TrainMiniBatchWithTrace(
            items.Select(x => x.Example).ToList(),
            localEpochs,
            detail,
            Options.EvidenceTolerance,
            Options.RelevanceTolerance,
            Options.LossStopThreshold);
        trainWatch.Stop();
        timings.AddTrain(trainWatch.ElapsedMilliseconds);
        timings.AddExampleCost(trainingTrace.FinalAverageCost);

        foreach (PendingTrainItem item in items)
        {
            ChatMonitoringNeuralModelPrediction after = telemetry.Predict(item.TrainingInput);
            bool accepted = Math.Abs(after.Evidence - item.Evaluation.TargetScore) <= Options.EvidenceTolerance
                && Math.Abs(after.Relevance - item.Evaluation.TargetRelevance) <= Options.RelevanceTolerance;
            SyntheticEvaluatorResult evaluation = item.Evaluation;
            if (ShouldAudit(session.SessionId, item.TicketIndex, item.Message.MessageIndex, run.ChatMonitoringKind))
                evaluation = await MaybeAuditAsync(item.Ticket, item.Message, item.Requirement, item.InitialInference.Prediction, evaluation, timings, ct);

            TicketModelTrainingExample record = new()
            {
                TrainingExampleId = Guid.NewGuid(), Requirement = item.Requirement, BootstrapMessage = item.Message.Content,
                TargetScore = evaluation.TargetScore, TargetRelevance = evaluation.TargetRelevance, Category = item.Ticket.Category,
                Source = "SyntheticLlmTraining", ApprovedAtUtc = DateTime.UtcNow, ApprovedByUserId = session.StartedByUserId,
                NeuralNetTrainingSessionId = session.SessionId, ChatMonitoringKind = run.ChatMonitoringKind,
                ContextSnapshot = item.Ticket.ContextSnapshot,
            };
            await batch.EnqueueAsync(record, item.Message.Content, ChatMonitoringVectorKeys.LineagePositionId(run.ChatMonitoringKind), ct);
            replay.AddPass(item.TicketIndex, item.Message, 1, item.Ticket, item.InitialInference, evaluation, item.Community, trainingTrace, accepted);
        }
    }

    private async Task<SyntheticEvaluatorResult> MaybeAuditAsync(
        SyntheticTicket generated,
        SyntheticThreadMessage message,
        string requirement,
        ChatMonitoringNeuralModelPrediction prediction,
        SyntheticEvaluatorResult evaluation,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        System.Diagnostics.Stopwatch auditWatch = System.Diagnostics.Stopwatch.StartNew();
        SyntheticEvaluatorResult? audit = null;
        for (int attempt = 0; attempt < 3 && audit is null; attempt++)
        {
            if (attempt > 0) timings.AddLlm2Retry();
            audit = await EvaluateSyntheticTicketAsync(generated with { Message = message.Content, Requirement = requirement }, prediction, ct);
        }
        auditWatch.Stop();
        timings.AddAudit(auditWatch.ElapsedMilliseconds);
        if (audit is not null)
            evaluation = evaluation with { Feedback = Truncate($"{evaluation.Feedback} | audit:{audit.Verdict}/{audit.Feedback}", 2000) };
        return evaluation;
    }

    private sealed record PendingTrainItem(
        int TicketIndex,
        SyntheticThreadMessage Message,
        SyntheticTicket Ticket,
        string Requirement,
        ChatMonitoringNeuralModelInput TrainingInput,
        ChatMonitoringNeuralModelTrainingExample Example,
        ChatMonitoringNeuralModelInferenceTrace InitialInference,
        SyntheticEvaluatorResult Evaluation,
        SyntheticCommunityResolution Community,
        bool FullTrace);

    private async Task PersistAsync(SemaphoreSlim persistenceGate, TrainingSessionTimings timings, CancellationToken ct)
    {
        await persistenceGate.WaitAsync(ct);
        try
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            await db.SaveChangesAsync(ct);
            watch.Stop();
            timings.AddDb(watch.ElapsedMilliseconds);
        }
        finally { persistenceGate.Release(); }
    }

    private bool ShouldCaptureFullTrace(Guid sessionId, int ticketIndex, int messageIndex, NeuralModelKindChatMonitoring kind)
    {
        if (!Options.CompactReplay) return true;
        double rate = Math.Clamp(Options.FullTraceSampleRate, 0, 1);
        if (rate <= 0) return false;
        int bucket = HashCode.Combine(sessionId, ticketIndex, messageIndex, (int)kind, 0x46554C4C);
        return (bucket & int.MaxValue) / (double)int.MaxValue < rate;
    }

    private bool ShouldAudit(Guid sessionId, int ticketIndex, int messageIndex, NeuralModelKindChatMonitoring kind)
    {
        double rate = Math.Clamp(Options.AuditSampleRate, 0, 1);
        if (rate <= 0) return false;
        int bucket = HashCode.Combine(sessionId, ticketIndex, messageIndex, (int)kind, 0x41554449);
        return (bucket & int.MaxValue) / (double)int.MaxValue < rate;
    }

    private static IReadOnlyList<(int TicketIndex, SyntheticTicket Ticket)> SelectTicketsForRun(
        Guid sessionId,
        NeuralModelKindChatMonitoring chatMonitoringKind,
        IReadOnlyList<(int TicketIndex, SyntheticTicket? Ticket)> tickets,
        double crossDomainSampleRate)
    {
        List<(int TicketIndex, SyntheticTicket Ticket)> matching = [];
        List<(int TicketIndex, SyntheticTicket Ticket)> cross = [];
        foreach ((int ticketIndex, SyntheticTicket? ticket) in tickets)
        {
            if (ticket is null) continue;
            if (IsModelDomainMatch(chatMonitoringKind, ticket.Category)) matching.Add((ticketIndex, ticket));
            else cross.Add((ticketIndex, ticket));
        }

        double rate = Math.Clamp(crossDomainSampleRate, 0, 1);
        int take = rate <= 0 || cross.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(cross.Count * rate));
        Random random = new(HashCode.Combine(sessionId, (int)chatMonitoringKind, 0x58444F4D));
        List<(int TicketIndex, SyntheticTicket Ticket)> sampled = cross.OrderBy(_ => random.Next()).Take(take).ToList();
        return matching.Concat(sampled).OrderBy(x => x.TicketIndex).ToList();
    }

    private static SyntheticEvaluatorResult ResolveTeacherEvaluation(
        SyntheticTicket ticket,
        SyntheticThreadMessage message,
        ChatMonitoringNeuralModelPrediction prediction,
        NeuralModelKindChatMonitoring chatMonitoringKind)
    {
        double target = message.TeacherEvidence ?? CreateFallbackEvaluation(ticket, message, prediction).TargetScore;
        double relevance = message.TeacherRelevance ?? message.ChannelRelevance;
        double approval = message.TeacherApprovalEstimate ?? message.CommunityIntent.ProposedApproval;
        double confidence = message.TeacherConfidence ?? .75;
        if (!IsModelDomainMatch(chatMonitoringKind, ticket.Category))
        {
            return new SyntheticEvaluatorResult("REVISE", .5, .08,
                "Cross-domain control: neutral evidence and low relevance.", .5, confidence);
        }

        bool accepted = Math.Abs(prediction.Evidence - target) < .12 && Math.Abs(prediction.Relevance - relevance) < .12;
        return new SyntheticEvaluatorResult(accepted ? "LGTM" : "REVISE", target, relevance,
            "Fixed teacher label (LLM-1 scenario or one-shot LLM-2 label).", approval, confidence);
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

    private async Task<SyntheticTicket?> GenerateSyntheticTicketAsync(NeuralTrainingMode mode, TrainingSessionTimings timings, CancellationToken ct)
    {
        SyntheticThreadScenario? scenario = await scenarioGenerator.GenerateAsync(mode, ct);
        SyntheticThreadMessage? primaryMessage = scenario?.Messages.FirstOrDefault(x => !x.IsDistractor)
            ?? scenario?.Messages.FirstOrDefault();
        if (scenario is not null && primaryMessage is not null)
        {
            IReadOnlyList<SyntheticThreadMessage> labeled = await EnsureTeacherLabelsAsync(scenario, timings, ct);
            string requirement = $"{scenario.Requirement}\nChannel: {primaryMessage.Channel}\nAuthor role: {primaryMessage.AuthorRole}";
            return new SyntheticTicket(
                scenario.Category,
                requirement,
                primaryMessage.Content,
                scenario.InitialContext,
                primaryMessage.TeacherEvidence ?? .5,
                primaryMessage.TeacherRelevance ?? primaryMessage.ChannelRelevance,
                labeled);
        }

        const string moderationFallbackPrompt =
            "Generate short fictional moderation-ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Scores are 0 to 1. Never include real personal data.";
        const string tutoringFallbackPrompt =
            "Generate short fictional tutor-application ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Use tutoring categories such as tutoring-mathematics or tutoring-science. Scores are 0 to 1. Never include real personal data.";
        // Both: randomly pick a domain so fallback tickets stay usable by either lineage.
        bool preferTutoring = mode switch
        {
            NeuralTrainingMode.Tutoring => true,
            NeuralTrainingMode.Moderation => false,
            _ => Random.Shared.Next(2) == 1,
        };
        string systemPrompt = preferTutoring ? tutoringFallbackPrompt : moderationFallbackPrompt;
        string userPrompt = preferTutoring
            ? "Create one varied school-chat tutor-application example."
            : "Create one varied school-chat moderation example.";
        string? response = await llm.ChatJsonAsync(systemPrompt, userPrompt, ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            string category = GetString(root, "category"), requirement = GetString(root, "requirement"), message = GetString(root, "message");
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(requirement) || string.IsNullOrWhiteSpace(message)) return null;
            float evidence = (float)GetUnit(root, "expectedScore", .5);
            float relevance = (float)GetUnit(root, "expectedRelevance", .5);
            SyntheticThreadMessage fallbackMessage = new(0, "synthetic-user", "student", "general", message[..Math.Min(4000, message.Length)], false, relevance, new(.5f, 10, .5f, []), evidence, relevance, evidence, .7f);
            return new(category[..Math.Min(80, category.Length)], requirement[..Math.Min(4000, requirement.Length)], message[..Math.Min(4000, message.Length)], Truncate(GetString(root, "contextSnapshot"), 2500), evidence, relevance, [fallbackMessage]);
        }
        catch (JsonException) { return null; }
    }

    private async Task<IReadOnlyList<SyntheticThreadMessage>> EnsureTeacherLabelsAsync(
        SyntheticThreadScenario scenario,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        List<SyntheticThreadMessage> labeled = [];
        foreach (SyntheticThreadMessage message in scenario.Messages)
        {
            if (message.TeacherEvidence is not null && message.TeacherRelevance is not null)
            {
                labeled.Add(message);
                continue;
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            SyntheticThreadMessage? fromLlm = await LabelMessageTeacherAsync(scenario, message, ct);
            watch.Stop();
            timings.AddTeacherLabel(watch.ElapsedMilliseconds);
            if (fromLlm is not null)
            {
                labeled.Add(fromLlm);
                continue;
            }

            SyntheticEvaluatorResult fallback = CreateFallbackEvaluation(
                new SyntheticTicket(scenario.Category, scenario.Requirement, message.Content, scenario.InitialContext, .5, message.ChannelRelevance, scenario.Messages),
                message,
                new ChatMonitoringNeuralModelPrediction(.5f, message.ChannelRelevance, .5f, NeuralModelKindChatMonitoring.Moderation, "label", "general", "fallback"));
            labeled.Add(message with
            {
                TeacherEvidence = (float)fallback.TargetScore,
                TeacherRelevance = (float)fallback.TargetRelevance,
                TeacherApprovalEstimate = (float)fallback.ApprovalEstimate,
                TeacherConfidence = (float)fallback.EvaluatorConfidence,
            });
        }

        return labeled;
    }

    private async Task<SyntheticThreadMessage?> LabelMessageTeacherAsync(
        SyntheticThreadScenario scenario,
        SyntheticThreadMessage message,
        CancellationToken ct)
    {
        const string systemPrompt = "You are labeling training targets for a school-chat classifier. Return JSON only: targetScore (0..1), targetRelevance (0..1), approvalEstimate (0..1), evaluatorConfidence (0..1), feedback. Do not grade a student model; produce the ideal evidence and relevance labels for this message.";
        string prompt = $"<requirement>{scenario.Requirement}</requirement>\n<context>{scenario.InitialContext}</context>\n<channel>{message.Channel}</channel>\n<authorRole>{message.AuthorRole}</authorRole>\n<isDistractor>{message.IsDistractor}</isDistractor>\n<message>{message.Content}</message>";
        string? response = await llm.ChatJsonAsync(systemPrompt, prompt, ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            return message with
            {
                TeacherEvidence = (float)GetUnit(root, "targetScore", .5),
                TeacherRelevance = (float)GetUnit(root, "targetRelevance", message.ChannelRelevance),
                TeacherApprovalEstimate = (float)GetUnit(root, "approvalEstimate", message.CommunityIntent.ProposedApproval),
                TeacherConfidence = (float)GetUnit(root, "evaluatorConfidence", .7),
            };
        }
        catch (JsonException) { return null; }
    }

    private async Task<SyntheticEvaluatorResult?> EvaluateSyntheticTicketAsync(SyntheticTicket ticket, ChatMonitoringNeuralModelPrediction prediction, CancellationToken ct)
    {
        const string systemPrompt = "You are an independent evaluator for a small school-chat classifier. Return JSON only: verdict (LGTM or REVISE), targetScore (0..1), targetRelevance (0..1), approvalEstimate (0..1), evaluatorConfidence (0..1), feedback. You receive no proposed vote data. Use LGTM only if the student classification is sufficiently correct.";
        string prompt = $"<requirement>{ticket.Requirement}</requirement>\n<context>{ticket.ContextSnapshot}</context>\n<message>{ticket.Message}</message>\n<student_score>{prediction.Evidence:F3}</student_score>\n<student_relevance>{prediction.Relevance:F3}</student_relevance>\n<student_confidence>{prediction.Confidence:F3}</student_confidence>";
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
                GetUnit(root, "targetScore", prediction.Evidence),
                GetUnit(root, "targetRelevance", prediction.Relevance),
                Truncate(GetString(root, "feedback"), 2000),
                GetUnit(root, "approvalEstimate", .5),
                GetUnit(root, "evaluatorConfidence", prediction.Confidence));
        }
        catch (JsonException) { return null; }
    }

    private static SyntheticEvaluatorResult CreateFallbackEvaluation(SyntheticTicket ticket, SyntheticThreadMessage message, ChatMonitoringNeuralModelPrediction prediction)
    {
        string text = message.Content.ToLowerInvariant();
        bool moderation = ticket.Category.Contains("moderation", StringComparison.OrdinalIgnoreCase);
        bool concerning = moderation && (text.Contains("damn") || text.Contains("hell") || text.Contains("idiot") || text.Contains("stupid"));
        bool incorrectMath = !moderation && message.Channel.Contains("math", StringComparison.OrdinalIgnoreCase) && (text.Contains("8 × 7 is 54") || text.Contains("8 x 7 is 54"));
        double target = concerning ? .95 : incorrectMath ? .12 : message.IsDistractor ? .5 : .82;
        double relevance = message.IsDistractor ? .08 : message.ChannelRelevance;
        bool accepted = Math.Abs(prediction.Evidence - target) < .12 && Math.Abs(prediction.Relevance - relevance) < .12;
        return new SyntheticEvaluatorResult(accepted ? "LGTM" : "REVISE", target, relevance,
            "Deterministic reviewer fallback used because LLM 2 returned no valid JSON.", target, .65);
    }

    private static string GetString(JsonElement root, string property) => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double GetUnit(JsonElement root, string property, double fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? Math.Clamp(result, 0, 1) : fallback;
    private static IReadOnlyList<NeuralModelKindChatMonitoring> GetChatMonitoringKinds(NeuralTrainingMode mode) => mode switch
    {
        NeuralTrainingMode.Moderation => [NeuralModelKindChatMonitoring.Moderation],
        NeuralTrainingMode.Tutoring => [NeuralModelKindChatMonitoring.Tutoring],
        _ => [NeuralModelKindChatMonitoring.Moderation, NeuralModelKindChatMonitoring.Tutoring],
    };

    private static bool IsModelDomainMatch(NeuralModelKindChatMonitoring chatMonitoringKind, string category)
    {
        bool tutoringScenario = category.Contains("tutor", StringComparison.OrdinalIgnoreCase)
            || category.Contains("competency", StringComparison.OrdinalIgnoreCase)
            || category.StartsWith("tutoring-", StringComparison.OrdinalIgnoreCase);
        if (chatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring)
            return tutoringScenario;

        string normalized = ChatMonitoringCategoryTaxonomy.NormalizeCategory(
            NeuralModelKindChatMonitoring.Moderation, category);
        if (ChatMonitoringModerationConcepts.TryGet(normalized, out _)
            || string.Equals(normalized, ChatMonitoringModerationConcepts.CatchAll, StringComparison.Ordinal))
            return true;

        return category.Contains("moderation", StringComparison.OrdinalIgnoreCase)
            || category.Contains("harassment", StringComparison.OrdinalIgnoreCase)
            || category.Contains("profanity", StringComparison.OrdinalIgnoreCase)
            || category.Contains("spam", StringComparison.OrdinalIgnoreCase)
            || !tutoringScenario;
    }

    private static NeuralNetTrainingSessionDto MapSession(NeuralNetTrainingSession session, IEnumerable<ChatMonitoringNeuralModelRun>? runs = null) => new()
    {
        SessionId = session.SessionId, RequestedTicketCount = session.RequestedTicketCount, MaxPassesPerTicket = session.MaxPassesPerTicket, Mode = session.Mode, Status = session.Status,
        CreatedAtUtc = session.CreatedAtUtc, StartedAtUtc = session.StartedAtUtc, CompletedAtUtc = session.CompletedAtUtc, FailureReason = session.FailureReason, HasReport = !string.IsNullOrWhiteSpace(session.ReportJson),
        ChatMonitoringRuns = (runs ?? []).OrderBy(x => x.ChatMonitoringKind).Select(x => new ChatMonitoringNeuralModelRunDto
        {
            ChatMonitoringKind = x.ChatMonitoringKind,
            Status = x.Status,
            CanonicalGeneration = x.CanonicalGeneration,
            HasWorkerReplay = !string.IsNullOrWhiteSpace(x.WorkerReplayJson),
            HasPromotionReplay = !string.IsNullOrWhiteSpace(x.PromotionReplayJson),
            FailureReason = x.FailureReason,
        }).ToList(),
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private sealed record SyntheticTicket(string Category, string Requirement, string Message, string ContextSnapshot, double ExpectedScore, double ExpectedRelevance, IReadOnlyList<SyntheticThreadMessage> Messages);
    private sealed record SyntheticEvaluatorResult(string Verdict, double TargetScore, double TargetRelevance, string Feedback, double ApprovalEstimate, double EvaluatorConfidence);
    private sealed class ReplayBuilder
    {
        private readonly NeuralNetTrainingSession session;
        private readonly IChatMonitoringNeuralModelTelemetry telemetry;
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

        public ReplayBuilder(NeuralNetTrainingSession session, IChatMonitoringNeuralModelTelemetry telemetry)
        {
            this.session = session; this.telemetry = telemetry;
            initial = telemetry.GetParameterSnapshot(0, 0);
        }

        public void BeginTicket(int ticketIndex, SyntheticTicket ticket) => tickets.Add(new TicketState(ticketIndex, ticket, []));

        public void AddPass(int ticketIndex, SyntheticThreadMessage message, int passIndex, SyntheticTicket ticket,
            ChatMonitoringNeuralModelInferenceTrace initialInference, SyntheticEvaluatorResult evaluation,
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
            int verdict = verdicts.Count; verdicts.Add(new(accepted, Intern(accepted ? "Prediction within teacher-label / loss tolerance." : evaluation.Feedback), (float)evaluation.TargetScore, .75f, iterations.Count, initialForward, finalForward));
            Frame(ReplayPhase.FinalVerdict, ReplayPayloadKind.FinalVerdict, ticketIndex, passIndex, message.MessageIndex, null, verdict);
            messageState.Passes.Add(new(passIndex, message.MessageIndex, inputIndex, initialForward, evaluationIndex, generationIndex, voteEvaluationIndex, samplingIndex, iterations, finalForward, telemetry.GetParameterSnapshot(null, localRevision)));
        }

        public NeuralNetReplayReportV2 Build(ReplayCompletionStatus status, ReplayFailure? failure = null, int epochs = 12)
        {
            NeuralNetParameterSnapshot final = telemetry.GetParameterSnapshot(null, localRevision);
            IReadOnlyList<TrainingTicketReplay> ticketReplay = tickets.Select(ticket => new TrainingTicketReplay(ticket.Index, Intern(ticket.Ticket.Category), Intern(ticket.Ticket.Requirement), Intern(ticket.Ticket.ContextSnapshot), ticket.Messages.Select(message => new TrainingMessageReplay(message.Message.MessageIndex, Intern(message.Message.AuthorId), Intern(message.Message.AuthorRole), Intern(message.Message.Channel), message.Message.IsDistractor, message.Message.ChannelRelevance, message.Passes)).ToList())).ToList();
            ReplayPayloadCollections payloads = new(inputs, forwards, evaluations, losses, backwards, updates, verdicts, voteGeneration, voteEvaluation, voteSampling);
            TrainingProvenance provenance = new(telemetry.GetStateSnapshot().ModelVersion, "hashed-text-48-v1", "bce+softmax-ce-avg-v1", "momentum-mini-batch-SGD", .035f, epochs, "hc-xoshiro256ss-v1", 0x48434D4C, "replay-v2-worker-v1");
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

    private sealed class PersistenceBatch(
        AppDbContext db,
        IVectorDocumentStore vectors,
        SemaphoreSlim persistenceGate,
        int batchSize,
        TrainingSessionTimings timings)
    {
        private readonly List<TicketModelTrainingExample> examples = [];
        private readonly List<(string Content, IReadOnlyList<float> Embedding, string PositionId, Guid CanonicalId, object Metadata)> pendingVectors = [];

        public async Task EnqueueAsync(TicketModelTrainingExample record, string content, string positionId, CancellationToken ct)
        {
            examples.Add(record);
            pendingVectors.Add((content, ChatMonitoringFeatureEncoder.EmbedText(content), positionId, record.TrainingExampleId,
                new { record.TrainingExampleId, record.Category, record.TargetScore, record.TargetRelevance, record.Source, record.ChatMonitoringKind }));
            if (examples.Count >= Math.Clamp(batchSize, 1, 500))
                await FlushAsync(ct);
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            if (examples.Count == 0 && pendingVectors.Count == 0) return;

            await persistenceGate.WaitAsync(ct);
            try
            {
                if (examples.Count > 0)
                {
                    List<TicketModelTrainingExample> toSave = [.. examples];
                    db.TicketModelTrainingExamples.AddRange(toSave);
                    System.Diagnostics.Stopwatch dbWatch = System.Diagnostics.Stopwatch.StartNew();
                    await db.SaveChangesAsync(ct);
                    dbWatch.Stop();
                    timings.AddDb(dbWatch.ElapsedMilliseconds);
                    timings.ExamplesPersisted += toSave.Count;
                    // Drop only after a successful DB commit so a failed save can retry.
                    examples.Clear();
                }

                if (pendingVectors.Count == 0) return;

                System.Diagnostics.Stopwatch vectorWatch = System.Diagnostics.Stopwatch.StartNew();
                while (pendingVectors.Count > 0)
                {
                    (string content, IReadOnlyList<float> embedding, string positionId, Guid canonicalId, object metadata) =
                        pendingVectors[0];
                    await vectors.UpsertAsync(
                        VectorNamespaces.TicketTrainingExample, content, embedding, positionId, canonicalId, metadata, ct);
                    pendingVectors.RemoveAt(0);
                }

                vectorWatch.Stop();
                timings.AddVector(vectorWatch.ElapsedMilliseconds);
            }
            finally { persistenceGate.Release(); }
        }
    }

    private sealed class TrainingSessionTimings
    {
        private readonly object gate = new();
        public long Llm1ScenarioMs;
        public long TeacherLabelMs;
        public long AuditMs;
        public long TrainMs;
        public long DbSaveMs;
        public long VectorUpsertMs;
        public int Llm2JsonRetries;
        public int ExamplesPersisted;
        public int AuditCount;
        public double CostSum;
        public int CostSamples;

        public void AddTeacherLabel(long ms) { lock (gate) TeacherLabelMs += ms; }
        public void AddAudit(long ms) { lock (gate) { AuditMs += ms; AuditCount++; } }
        public void AddLlm2Retry() { lock (gate) Llm2JsonRetries++; }
        public void AddTrain(long ms) { lock (gate) TrainMs += ms; }
        public void AddDb(long ms) { lock (gate) DbSaveMs += ms; }
        public void AddVector(long ms) { lock (gate) VectorUpsertMs += ms; }
        public void AddExampleCost(float totalLoss)
        {
            if (!float.IsFinite(totalLoss))
                return;
            lock (gate) { CostSum += totalLoss; CostSamples++; }
        }

        public object ToReport() => new
        {
            llm1ScenarioMs = Llm1ScenarioMs,
            teacherLabelMs = TeacherLabelMs,
            auditMs = AuditMs,
            trainMs = TrainMs,
            dbSaveMs = DbSaveMs,
            vectorUpsertMs = VectorUpsertMs,
            llm2JsonRetries = Llm2JsonRetries,
            examplesPersisted = ExamplesPersisted,
            auditCount = AuditCount,
            averageCost = CostSamples == 0 ? 0d : NeuralNetFinite.OrZero(CostSum / CostSamples),
            costSamples = CostSamples,
        };
    }
}
