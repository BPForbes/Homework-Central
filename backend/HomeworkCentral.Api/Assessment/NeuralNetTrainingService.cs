using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace HomeworkCentral.Api.Assessment;

public enum NeuralNetTrainingSessionRemovalResult
{
    Removed,
    NotFound,
    /// <summary>Claimed by the worker; deleting would race mid-training.</summary>
    Running,
}

/// <summary>
/// Admin training feedback, synthetic sessions, and replay reports.
/// Canonical promotion is handled by <see cref="NeuralNetTrainingPromoter"/>, not this service.
/// </summary>
public interface INeuralNetTrainingService
{
    Task<PagedResultDto<NeuralNetTrainingFeedbackDto>> GetPendingFeedbackAsync(
        DateTime? beforeUtc = null,
        int limit = 50,
        CancellationToken ct = default);
    Task<NeuralNetTrainingFeedbackDto> ApproveAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task RejectAsync(Guid scoreEventId, Guid actorUserId, CancellationToken ct = default);
    Task<NeuralNetDataManagementDto> GetDataManagementAsync(CancellationToken ct = default);
    Task<NeuralNetVisualizerDto> GetVisualizerAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingSessionDto> StartSyntheticSessionAsync(StartNeuralNetTrainingRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<NeuralNetTrainingSessionDto> ResumeTrainingSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<NeuralNetTrainingLiveProgressDto>> GetLiveProgressAsync(CancellationToken ct = default);
    Task<PagedResultDto<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(
        DateTime? beforeUtc = null,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// V2 replay JSON for a monitor kind, or legacy session report JSON when
    /// <paramref name="chatMonitoringKind"/> is null.
    /// </summary>
    Task<string?> GetSessionReportAsync(Guid sessionId, NeuralModelKindChatMonitoring? chatMonitoringKind = null, CancellationToken ct = default);

    Task RunSyntheticSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<bool> RunNextSyntheticSessionAsync(CancellationToken ct = default);
    Task<NeuralNetTrainingSessionRemovalResult> RemoveSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Stops a queued or running session. Continuous sessions end after the current
    /// ticket/message step finishes flushing. Only an explicit stop ends a session:
    /// evaluator feedback and generator failures never terminate training.
    /// </summary>
    Task<bool> StopTrainingSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class NeuralNetTrainingService(
    AppDbContext db,
    IChatMonitoringNeuralModelFactory chatMonitoringModels,
    IVectorDocumentStore vectors,
    ILlmClient llm,
    INeuralNetTrainingQueue queue,
    INeuralNetTrainingCancellationRegistry cancellationRegistry,
    INeuralNetTrainingLlmModule trainingLlm,
    NeuralNetTrainingPromoter promoter,
    INeuralNetTrainingProgressStore progressStore,
    Microsoft.Extensions.Options.IOptions<NeuralNetTrainingOptions> trainingOptions,
    Microsoft.Extensions.Logging.ILogger<NeuralNetTrainingService> logger) : INeuralNetTrainingService
{
    private NeuralNetTrainingOptions Options => trainingOptions.Value;
    public async Task<PagedResultDto<NeuralNetTrainingFeedbackDto>> GetPendingFeedbackAsync(
        DateTime? beforeUtc = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        int pageSize = ClampPageSize(limit);
        IQueryable<TicketMessageScore> query = PendingQuery();
        if (beforeUtc is not null)
            query = query.Where(x => x.CreatedAtUtc < beforeUtc.Value);

        List<TicketMessageScore> scores = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(ct);
        bool hasMore = scores.Count > pageSize;
        if (hasMore)
            scores = scores.Take(pageSize).ToList();

        Dictionary<Guid, string> messages = await LoadMessagesAsync(scores.Select(x => x.MessageId), ct);
        List<NeuralNetTrainingFeedbackDto> items = scores
            .Select(score => Map(score, messages.GetValueOrDefault(score.MessageId)))
            .ToList();
        DateTime? nextBeforeUtc = items.Count == 0 ? null : scores[^1].CreatedAtUtc;
        return new PagedResultDto<NeuralNetTrainingFeedbackDto>(items, hasMore, nextBeforeUtc, pageSize);
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
            // Trains the live shared model, so it must see the same vector space inference does.
            model.Train(
                new ChatMonitoringNeuralModelInput(
                    requirement,
                    score.ContextSnapshot ?? string.Empty,
                    message,
                    0,
                    1,
                    0,
                    .5f,
                    TextEmbedding: await llm.EmbedAsync(message, ct)),
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
        // Continuous = train until Stop. Prefer the flag; TicketCount <= 0 is also continuous
        // so a dropped `continuous` boolean still cannot collapse into a one-shot finite run.
        bool continuous = ResolveContinuousTraining(request.Continuous, request.TicketCount);
        if (TrainingPersistencePolicy.IsTrainingStartBlocked(
            progressStore.HasActiveTraining(),
            TrainingHeapPressure.ShouldSkipTraces()))
        {
            throw new InvalidOperationException(TrainingPersistencePolicy.HeapElevatedMessage);
        }

        NeuralNetTrainingSession session = new()
        {
            SessionId = Guid.NewGuid(), StartedByUserId = actorUserId,
            RequestedTicketCount = continuous ? 0 : Math.Clamp(request.TicketCount, 1, 10),
            MaxPassesPerTicket = continuous ? 1 : Math.Clamp(request.MaxPassesPerTicket, 1, 6),
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

    public async Task<NeuralNetTrainingSessionDto> ResumeTrainingSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        NeuralNetTrainingSession session = await db.NeuralNetTrainingSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Training session {sessionId} was not found.");

        if (!TrainingPersistencePolicy.CanResumeContinuousTraining(session.Status, session.RequestedTicketCount))
        {
            throw new InvalidOperationException("Only a cancelled continuous training session can be resumed.");
        }

        if (TrainingPersistencePolicy.IsTrainingStartBlocked(
            progressStore.HasActiveTraining(),
            TrainingHeapPressure.ShouldSkipTraces()))
        {
            throw new InvalidOperationException(TrainingPersistencePolicy.HeapElevatedMessage);
        }

        List<ChatMonitoringNeuralModelRun> runs = await db.ChatMonitoringNeuralModelRuns
            .Where(x => x.SessionId == sessionId)
            .ToListAsync(ct);

        session.Status = "Queued";
        session.FailureReason = null;
        session.CompletedAtUtc = null;
        session.StartedAtUtc = null;
        foreach (ChatMonitoringNeuralModelRun run in runs)
        {
            run.Status = "Queued";
            run.FailureReason = null;
            run.CompletedAtUtc = null;
            run.StartedAtUtc = null;
        }

        if (!queue.TryEnqueue(session.SessionId))
        {
            session.Status = "Failed";
            session.FailureReason = "The training queue is full. Retry after an in-flight session finishes.";
            session.CompletedAtUtc = DateTime.UtcNow;
            foreach (ChatMonitoringNeuralModelRun run in runs)
            {
                run.Status = "Failed";
                run.FailureReason = session.FailureReason;
                run.CompletedAtUtc = session.CompletedAtUtc;
            }
        }

        await db.SaveChangesAsync(ct);
        if (session.Status == "Failed")
        {
            throw new InvalidOperationException(session.FailureReason ?? "The training queue is full.");
        }

        return MapSession(session, runs);
    }

    public Task<IReadOnlyList<NeuralNetTrainingLiveProgressDto>> GetLiveProgressAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<NeuralNetTrainingLiveProgressDto> live = progressStore.GetAll()
            .Select(MapLiveProgress)
            .OfType<NeuralNetTrainingLiveProgressDto>()
            .ToList();
        return Task.FromResult(live);
    }

    /// <summary>
    /// Lists recent sessions without materializing replay JSON. Worker replays can reach tens of
    /// megabytes per run, so the poll endpoint projects presence flags instead of the payloads;
    /// selecting the blobs here exhausted memory and timed out the connection during long sessions.
    /// </summary>
    public async Task<PagedResultDto<NeuralNetTrainingSessionDto>> GetTrainingSessionsAsync(
        DateTime? beforeUtc = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        int pageSize = ClampPageSize(limit);
        IQueryable<NeuralNetTrainingSession> query = db.NeuralNetTrainingSessions.AsNoTracking();
        if (beforeUtc is not null)
            query = query.Where(x => x.CreatedAtUtc < beforeUtc.Value);

        List<SessionSummary> sessions = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(pageSize + 1)
            .Select(x => new SessionSummary(
                x.SessionId,
                x.RequestedTicketCount,
                x.MaxPassesPerTicket,
                x.Mode,
                x.Status,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.FailureReason,
                x.ReportJson != null))
            .ToListAsync(ct);

        bool hasMore = sessions.Count > pageSize;
        if (hasMore)
            sessions = sessions.Take(pageSize).ToList();

        Guid[] sessionIds = sessions.Select(x => x.SessionId).ToArray();
        List<RunSummary> runs = sessionIds.Length == 0
            ? []
            : await db.ChatMonitoringNeuralModelRuns.AsNoTracking()
                .Where(x => sessionIds.Contains(x.SessionId))
                .Select(x => new RunSummary(
                    x.SessionId,
                    x.ChatMonitoringKind,
                    x.Status,
                    x.CanonicalGeneration,
                    x.WorkerReplayJson != null,
                    x.PromotionReplayJson != null,
                    x.FailureReason))
                .ToListAsync(ct);

        List<NeuralNetTrainingSessionDto> items = sessions
            .Select(session => MapSessionSummary(
                session,
                runs.Where(run => run.SessionId == session.SessionId)))
            .ToList();
        DateTime? nextBeforeUtc = items.Count == 0 ? null : sessions[^1].CreatedAtUtc;
        return new PagedResultDto<NeuralNetTrainingSessionDto>(items, hasMore, nextBeforeUtc, pageSize);
    }

    private static int ClampPageSize(int limit) => limit is > 0 and <= 100 ? limit : 50;

    private sealed record SessionSummary(
        Guid SessionId,
        int RequestedTicketCount,
        int MaxPassesPerTicket,
        NeuralTrainingMode Mode,
        string Status,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        string? FailureReason,
        bool HasReport);

    private sealed record RunSummary(
        Guid SessionId,
        NeuralModelKindChatMonitoring ChatMonitoringKind,
        string Status,
        long? CanonicalGeneration,
        bool HasWorkerReplay,
        bool HasPromotionReplay,
        string? FailureReason);

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
        progressStore.Clear(sessionId);
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
        NeuralNetTrainingSession? session = await TryClaimSyntheticSessionAsync(sessionId, ct);
        if (session is null) return;

        CancellationToken sessionToken = cancellationRegistry.Link(sessionId, ct);
        TrainingSessionTimings timings = new();
        try
        {
            // Continuous sessions must not FailSyntheticSessionAsync on transient step errors —
            // only an explicit stop (or host shutdown) ends them.
            if (IsContinuousSession(session))
            {
                try
                {
                    SyntheticGeneratorFeedbackBuffer feedback = new();
                    await RunContinuousSyntheticSessionAsync(session, timings, feedback, sessionToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Defensive: continuous loop should absorb step failures; if something escapes,
                    // keep the session alive only until cancel — never mark Completed/Failed here.
                    logger.LogError(
                        ex,
                        "Continuous training session {SessionId} hit an unexpected outer failure; waiting for stop.",
                        sessionId);
                    await WaitUntilContinuousCancelledAsync(sessionToken);
                    throw new OperationCanceledException(sessionToken);
                }

                // RunContinuous only returns after cancellation throws; treat a clean return as stop.
                throw new OperationCanceledException(sessionToken);
            }

            await OperationalExceptionGuard.RunAsync(
                async () =>
                {
                    SyntheticGeneratorFeedbackBuffer feedback = new();
                    List<(int TicketIndex, SyntheticTicket? Ticket)> tickets =
                        await GenerateSyntheticTicketsAsync(session, timings, feedback, sessionToken);
                    await RunChatMonitoringRunsAsync(session, tickets, timings, feedback, sessionToken);
                    await CompleteSyntheticSessionAsync(session, timings, sessionToken);
                    PublishProgress(session, progress => progress with
                    {
                        Phase = "Completed",
                        TicketsProcessed = progress.TicketsProcessed,
                    });
                    await promoter.QueueSessionAsync(session.SessionId, sessionToken);
                },
                ex => FailSyntheticSessionAsync(session, timings, ex));
        }
        catch (OperationCanceledException)
        {
            await CancelSyntheticSessionAsync(session, timings);
            // Host shutdown cancels the linked token via `ct`; session-only cancel must not stop the worker.
            if (ct.IsCancellationRequested)
                throw;
        }
        finally
        {
            cancellationRegistry.Unregister(sessionId);
        }
    }

    private static bool IsContinuousSession(NeuralNetTrainingSession session) =>
        session.RequestedTicketCount == 0;

    /// <summary>
    /// Continuous when the client sets the flag or sends a non-positive ticket budget.
    /// Finite runs always clamp to at least one ticket.
    /// </summary>
    public static bool ResolveContinuousTraining(bool continuousFlag, int ticketCount) =>
        continuousFlag || ticketCount <= 0;

    /// <summary>Blocks until the continuous session token is cancelled (Stop or host shutdown).</summary>
    private static async Task WaitUntilContinuousCancelledAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public async Task<bool> StopTrainingSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        NeuralNetTrainingSession? session = await db.NeuralNetTrainingSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (session is null)
            return false;

        bool queued = string.Equals(session.Status, "Queued", StringComparison.OrdinalIgnoreCase);
        bool running = string.Equals(session.Status, "Running", StringComparison.OrdinalIgnoreCase);
        if (!queued && !running)
            return false;

        if (running && cancellationRegistry.TryCancel(sessionId))
            return true;

        // Queued, or Running with no live worker (the process that owned it went away). Mark the
        // session stopped directly so the admin UI is not stuck on an unstoppable row.
        queue.TryRemove(sessionId);
        string reason = queued ? "Training stopped before start." : "Training stopped by an administrator.";
        DateTime stoppedAt = DateTime.UtcNow;
        session.Status = "Cancelled";
        session.CompletedAtUtc = stoppedAt;
        session.FailureReason = reason;
        await db.ChatMonitoringNeuralModelRuns
            .Where(x => x.SessionId == sessionId && (x.Status == "Queued" || x.Status == "Running"))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, "Cancelled")
                    .SetProperty(x => x.CompletedAtUtc, stoppedAt)
                    .SetProperty(x => x.FailureReason, reason),
                ct);
        await db.SaveChangesAsync(ct);
        progressStore.Clear(sessionId);
        return true;
    }

    /// <summary>
    /// Trains one synthetic ticket (single message) at a time until Stop cancels the session token.
    /// Generator failures and train-step exceptions never complete or fail the session.
    /// SQL is persist-on-stop except for a weights-only heap spill. Replay JSON is not
    /// written mid-run; continuous Stop keeps <c>spill-checkpoint-v1</c>.
    /// </summary>
    private async Task RunContinuousSyntheticSessionAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings,
        SyntheticGeneratorFeedbackBuffer feedback,
        CancellationToken ct)
    {
        List<ChatMonitoringNeuralModelRun> runs = await db.ChatMonitoringNeuralModelRuns
            .Where(x => x.SessionId == session.SessionId)
            .OrderBy(x => x.ChatMonitoringKind)
            .ToListAsync(ct);
        using SemaphoreSlim persistenceGate = new(1, 1);
        SyntheticConceptCoverageSampler coverage = new(HashCode.Combine(session.SessionId, 0x434F5645));
        List<ChatMonitoringRunContext> contexts = [];

        foreach (ChatMonitoringNeuralModelRun run in runs)
        {
            IChatMonitoringNeuralModelTelemetry telemetry = ResolveChatMonitoringTelemetry(run);
            run.Status = "Running";
            run.StartedAtUtc = DateTime.UtcNow;
            RestoreSpillCheckpoint(run, telemetry);
            ReplayBuilder replay = new(session, telemetry);
            contexts.Add(new ChatMonitoringRunContext(
                session,
                run,
                telemetry,
                replay,
                new PersistenceBatch(db, vectors, persistenceGate, Options.PersistenceBatchSize, timings),
                [],
                timings,
                feedback));
        }

        PublishProgress(session, progress => progress with
        {
            Phase = "Continuous training",
            TicketsRequested = 0,
            TicketsGenerated = 0,
            TicketsProcessed = 0,
            MessagesProcessed = 0,
            GeneratorHints = feedback.Hints.ToList(),
        });

        int ticketIndex = 0;
        try
        {
            // Continuous has no ticket budget — only session cancel (Stop) or host shutdown exits.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                ticketIndex++;
                try
                {
                    if (TrainingHeapPressure.ShouldAttemptSpill())
                    {
                        bool spilled = await TrySpillTrainingHeapAsync(
                            session, contexts, persistenceGate, timings, ticketIndex, ct);
                        if (TrainingHeapSpill.AfterProactiveAttempt(spilled) == TrainingStepAfterSpill.Stop)
                            throw new TrainingHeapSpillFailedException(
                                "Training heap spill failed; waiting for Stop.");
                    }

                    await RunContinuousTrainingStepAsync(
                        session,
                        timings,
                        feedback,
                        coverage,
                        contexts,
                        persistenceGate,
                        ticketIndex,
                        ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (AggregateException aggregate) when (ct.IsCancellationRequested
                    || aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException))
                {
                    throw new OperationCanceledException(ct);
                }
                catch (OutOfMemoryException ex)
                {
                    logger.LogWarning(
                        ex,
                        "Continuous training step {TicketIndex} for session {SessionId} exhausted the heap; spilling.",
                        ticketIndex,
                        session.SessionId);
                    bool spilled = await TrySpillTrainingHeapAsync(
                        session, contexts, persistenceGate, timings, ticketIndex, CancellationToken.None);
                    if (TrainingHeapSpill.AfterOutOfMemory(spilled) == TrainingStepAfterSpill.Stop)
                        throw;

                    PublishProgress(session, progress => progress with
                    {
                        Phase = "Continuous training · heap spilled, continuing",
                        TicketsRequested = ticketIndex,
                        TicketsGenerated = ticketIndex,
                        LatestTrainingLlmSummary = Truncate(
                            $"Ticket {ticketIndex}: heap spilled to the database; continuing without retrying that step.", 280),
                        GeneratorHints = feedback.Hints.ToList(),
                    });
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
                catch (TrainingHeapSpillFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Continuous training step {TicketIndex} for session {SessionId} failed; continuing until stop.",
                        ticketIndex,
                        session.SessionId);
                    PublishProgress(session, progress => progress with
                    {
                        Phase = "Continuous training · step error, retrying",
                        TicketsRequested = ticketIndex,
                        TicketsGenerated = ticketIndex,
                        LatestTrainingLlmSummary = Truncate(
                            $"Ticket {ticketIndex}: step error — {ex.Message}", 280),
                        GeneratorHints = feedback.Hints.ToList(),
                    });
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            foreach (ChatMonitoringRunContext runContext in contexts)
            {
                await OperationalExceptionGuard.RunAsync(
                    async () =>
                    {
                        runContext.PendingTrain.Clear();
                        runContext.Run.Status = "Cancelled";
                        runContext.Run.CompletedAtUtc = DateTime.UtcNow;
                        runContext.Run.FailureReason ??= "Training cancelled.";
                        // Release traces first so GetParameterSnapshot can allocate on a dying heap.
                        if (!WriteSpillCheckpoint(session, runContext, ticketIndex, releaseHeapFirst: true))
                        {
                            logger.LogWarning(
                                "Stop-path spill checkpoint was not written for run {Kind}.",
                                runContext.Run.ChatMonitoringKind);
                        }

                        if (!TrainingHeapPressure.ShouldSkipTraces())
                            await runContext.Batch.FlushAsync(CancellationToken.None);
                    },
                    ex =>
                    {
                        logger.LogWarning(ex, "Failed to finalize cancelled continuous run {Kind}.", runContext.Run.ChatMonitoringKind);
                        return Task.CompletedTask;
                    });
            }

            await PersistAsync(persistenceGate, timings, CancellationToken.None);
            throw;
        }
    }

    private async Task RunContinuousTrainingStepAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings,
        SyntheticGeneratorFeedbackBuffer feedback,
        SyntheticConceptCoverageSampler coverage,
        List<ChatMonitoringRunContext> contexts,
        SemaphoreSlim persistenceGate,
        int ticketIndex,
        CancellationToken ct)
    {
        string targetCategory = coverage.NextTarget(session.Mode, ticketIndex);

        System.Diagnostics.Stopwatch llm1Watch = System.Diagnostics.Stopwatch.StartNew();
        SyntheticTicket? generated = await GenerateSyntheticTicketAsync(
            session.Mode, timings, feedback.Hints, targetCategory, ct);
        llm1Watch.Stop();
        timings.TrainingLlmScenarioMs += llm1Watch.ElapsedMilliseconds;

        if (generated is null)
        {
            PublishProgress(session, progress => progress with
            {
                Phase = "Continuous training · waiting on generator",
                TicketsRequested = ticketIndex,
                TicketsGenerated = ticketIndex,
                LatestTrainingLlmSummary = $"Ticket {ticketIndex}: generation failed (target {targetCategory})",
                GeneratorHints = feedback.Hints.ToList(),
            });
            // Back off so an offline LLM does not spin the loop at full speed.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return;
        }

        SyntheticTicket singleMessageTicket = ToSingleMessageTicket(generated);
        if (ShouldSampleGeneratorAudit(session.SessionId, ticketIndex))
        {
            // A REVISE verdict reworks the prompt in place; the loop always keeps training.
            singleMessageTicket = ToSingleMessageTicket(await CollectBalancedGeneratorAuditAsync(
                session, singleMessageTicket, feedback, timings, coverage, ct));
        }
        else
        {
            feedback.RecordCoverageGaps(
                coverage.Underrepresented(
                    IsModelDomainMatch(NeuralModelKindChatMonitoring.Tutoring, singleMessageTicket.Category)
                        ? NeuralModelKindChatMonitoring.Tutoring
                        : NeuralModelKindChatMonitoring.Moderation));
        }

        PublishProgress(session, progress => progress with
        {
            Phase = "Continuous training",
            TicketsRequested = ticketIndex,
            TicketsGenerated = ticketIndex,
            LatestTrainingLlmSummary =
                $"Ticket {ticketIndex}: {singleMessageTicket.Category} · 1 message",
            CurrentEvaluationData = FormatEvaluationData(ticketIndex, singleMessageTicket, singleMessageTicket.Messages.FirstOrDefault()),
            GeneratorHints = feedback.Hints.ToList(),
        });

        List<ChatMonitoringRunContext> selected = contexts
            .Where(runContext => ShouldTrainContinuousTicket(
                session.SessionId,
                ticketIndex,
                runContext.Run.ChatMonitoringKind,
                singleMessageTicket.Category))
            .ToList();

        if (TrainingHeapPressure.ShouldSkipTraces())
        {
            foreach (ChatMonitoringRunContext runContext in selected)
                await TrainContinuousContextAsync(session, runContext, ticketIndex, singleMessageTicket, ct);
        }
        else
        {
            await Task.WhenAll(selected.Select(runContext =>
                TrainContinuousContextAsync(session, runContext, ticketIndex, singleMessageTicket, ct)));
        }

        PublishProgress(session, progress => progress with
        {
            Phase = "Continuous training",
            TicketsRequested = ticketIndex,
            TicketsGenerated = ticketIndex,
            TicketsProcessed = ticketIndex,
            MessagesProcessed = progress.MessagesProcessed + 1,
            ExamplesPersisted = timings.ExamplesPersisted,
            GeneratorHints = feedback.Hints.ToList(),
        });
    }

    private async Task TrainContinuousContextAsync(
        NeuralNetTrainingSession session,
        ChatMonitoringRunContext runContext,
        int ticketIndex,
        SyntheticTicket singleMessageTicket,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        NeuralNetTrainingLiveProgress? existing = progressStore.Get(session.SessionId);
        IReadOnlyList<int> layerWidths = existing?.LayerWidths ?? [];
        IReadOnlyList<string> layerLabels = existing?.LayerLabels ?? [];
        if (layerWidths.Count == 0)
        {
            ChatMonitoringNeuralModelStateSnapshot topologyState = runContext.Telemetry.GetStateSnapshot();
            layerWidths = topologyState.LayerWidths;
            layerLabels = topologyState.LayerLabels;
        }

        PublishProgress(session, progress => progress with
        {
            Phase = $"Continuous · {runContext.Run.ChatMonitoringKind}",
            ActiveChatMonitoringKind = runContext.Run.ChatMonitoringKind.ToString(),
            PathTone = "forward",
            LayerWidths = layerWidths,
            LayerLabels = layerLabels,
        });

        await ProcessSyntheticTicketAsync(
            runContext, ticketIndex, singleMessageTicket, miniBatchSize: 1, ct);
        await FlushPendingTrainingAsync(runContext, ct);
    }

    private bool ShouldTrainContinuousTicket(
        Guid sessionId,
        int ticketIndex,
        NeuralModelKindChatMonitoring kind,
        string category)
    {
        if (IsModelDomainMatch(kind, category))
            return true;

        double rate = Math.Clamp(Options.CrossDomainSampleRate, 0, 1);
        if (rate <= 0)
            return false;

        int bucket = HashCode.Combine(sessionId, ticketIndex, (int)kind, 0x4354524E);
        return (bucket & int.MaxValue) / (double)int.MaxValue < rate;
    }

    private static SyntheticTicket ToSingleMessageTicket(SyntheticTicket ticket)
    {
        SyntheticThreadMessage primary = ticket.Messages.FirstOrDefault(message => !message.IsDistractor)
            ?? ticket.Messages.First();
        SyntheticThreadMessage normalized = primary with { MessageIndex = 0 };
        return ticket with
        {
            Message = normalized.Content,
            ExpectedScore = normalized.TeacherEvidence ?? ticket.ExpectedScore,
            ExpectedRelevance = normalized.TeacherRelevance ?? ticket.ExpectedRelevance,
            Messages = [normalized],
        };
    }

    private async Task<NeuralNetTrainingSession?> TryClaimSyntheticSessionAsync(
        Guid sessionId,
        CancellationToken ct)
    {
        DateTime claimedAt = DateTime.UtcNow;
        int claimed = await db.NeuralNetTrainingSessions
            .Where(x => x.SessionId == sessionId && x.Status == "Queued")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Running")
                .SetProperty(x => x.StartedAtUtc, claimedAt), ct);
        if (claimed == 0) return null;

        return await db.NeuralNetTrainingSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
    }

    private async Task<List<(int TicketIndex, SyntheticTicket? Ticket)>> GenerateSyntheticTicketsAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings,
        SyntheticGeneratorFeedbackBuffer feedback,
        CancellationToken ct)
    {
        // Sequential generation so balanced self-critique notes can steer later training-LLM scenarios
        // without concurrent prompt races. Forced taxonomy targets keep every filterable
        // moderation/tutoring concept in the session mix (not just payment-solicitation).
        List<(int TicketIndex, SyntheticTicket? Ticket)> tickets = [];
        SyntheticConceptCoverageSampler coverage = new(HashCode.Combine(session.SessionId, 0x434F5645));
        PublishProgress(session, progress => progress with
        {
            Phase = "Training LLM · scenario generation",
            TicketsRequested = session.RequestedTicketCount,
            TicketsGenerated = 0,
            GeneratorHints = feedback.Hints.ToList(),
        });

        System.Diagnostics.Stopwatch llm1Watch = System.Diagnostics.Stopwatch.StartNew();
        for (int ticketIndex = 1; ticketIndex <= session.RequestedTicketCount; ticketIndex++)
        {
            ct.ThrowIfCancellationRequested();
            string targetCategory = coverage.NextTarget(session.Mode, ticketIndex);
            SyntheticTicket? ticket = await GenerateSyntheticTicketAsync(
                session.Mode, timings, feedback.Hints, targetCategory, ct);
            if (ticket is null)
            {
                tickets.Add((ticketIndex, null));
                PublishProgress(session, progress => progress with
                {
                    Phase = "Training LLM · scenario generation",
                    TicketsGenerated = ticketIndex,
                    LatestTrainingLlmSummary = $"Ticket {ticketIndex}: generation failed (target {targetCategory})",
                    GeneratorHints = feedback.Hints.ToList(),
                });
                continue;
            }

            if (ShouldSampleGeneratorAudit(session.SessionId, ticketIndex))
                ticket = await CollectBalancedGeneratorAuditAsync(session, ticket, feedback, timings, coverage, ct);
            else
                feedback.RecordCoverageGaps(
                    coverage.Underrepresented(
                        IsModelDomainMatch(NeuralModelKindChatMonitoring.Tutoring, ticket.Category)
                            ? NeuralModelKindChatMonitoring.Tutoring
                            : NeuralModelKindChatMonitoring.Moderation));

            SyntheticTicket resolved = ticket;
            tickets.Add((ticketIndex, resolved));
            PublishProgress(session, progress => progress with
            {
                Phase = "Training LLM · scenario generation",
                TicketsGenerated = ticketIndex,
                LatestTrainingLlmSummary =
                    $"Ticket {ticketIndex}: {resolved.Category} · {resolved.Messages.Count} messages",
                CurrentEvaluationData = FormatEvaluationData(ticketIndex, resolved, resolved.Messages.FirstOrDefault()),
                GeneratorHints = feedback.Hints.ToList(),
            });
        }

        llm1Watch.Stop();
        timings.TrainingLlmScenarioMs += llm1Watch.ElapsedMilliseconds;
        return tickets;
    }

    /// <summary>
    /// Uses the training LLM's embedded selfCritique (same generation call) to steer later tickets and
    /// optionally rewrite the prompt. No second Ollama evaluator round-trip. A REVISE verdict
    /// never blocks the pipeline: the same module reworks the scenario and training continues with
    /// whichever attempt survives. REVISE / REINTERPRET steps publish amber (reeval) mesh lighting
    /// and append explicit audit-feed lines so the UI can show feedback, not only LGTM.
    /// </summary>
    private async Task<SyntheticTicket> CollectBalancedGeneratorAuditAsync(
        NeuralNetTrainingSession session,
        SyntheticTicket ticket,
        SyntheticGeneratorFeedbackBuffer feedback,
        TrainingSessionTimings timings,
        SyntheticConceptCoverageSampler coverage,
        CancellationToken ct)
    {
        NeuralModelKindChatMonitoring auditKind = IsModelDomainMatch(
            NeuralModelKindChatMonitoring.Tutoring, ticket.Category)
            ? NeuralModelKindChatMonitoring.Tutoring
            : NeuralModelKindChatMonitoring.Moderation;

        SyntheticTicket current = ticket;
        int maxRevisions = Math.Clamp(Options.GeneratorRevisionMaxAttempts, 0, 3);
        for (int attempt = 0; attempt <= maxRevisions; attempt++)
        {
            SyntheticEvaluatorResult audit = trainingLlm.CritiqueTicket(current);
            feedback.RecordAudit(audit.Verdict, audit.Feedback, current.Category);
            feedback.RecordCoverageGaps(coverage.Underrepresented(auditKind));

            bool needsRevision = audit.Verdict.Contains("REVISE", StringComparison.OrdinalIgnoreCase);
            string attemptTag = attempt == 0 ? "generate+evaluate" : $"after reinterpret {attempt}";
            string auditLine = Truncate(
                needsRevision
                    ? $"[{current.Category}] REVISE ({attemptTag}): {audit.Feedback}"
                    : $"[{current.Category}] LGTM ({attemptTag}): {audit.Feedback}",
                400);

            PublishProgress(session, progress =>
            {
                List<string> auditFeed = AppendAuditFeedLine(progress.AuditFeedbackFeed, auditLine);
                return WithReevalMesh(
                    progress with
                    {
                        Phase = needsRevision
                            ? "Training LLM · self-critique REVISE"
                            : "Training LLM · self-critique LGTM",
                        AuditsCompleted = progress.AuditsCompleted + 1,
                        LatestAuditFeedback = Truncate($"{audit.Verdict}: {audit.Feedback}", 280),
                        AuditFeedbackFeed = auditFeed,
                        CurrentEvaluationData = FormatEvaluationData(
                            progress.TicketsGenerated, current, current.Messages.FirstOrDefault()),
                        GeneratorHints = feedback.Hints.ToList(),
                        PathTone = needsRevision ? "reeval" : "accepted",
                    },
                    lightFullMesh: needsRevision);
            });

            // Hold amber long enough for the 2s live poll to paint yellow neurons before rewrite.
            if (needsRevision)
                await Task.Delay(TimeSpan.FromMilliseconds(1250), ct);

            if (!needsRevision || attempt == maxRevisions)
                return current;

            SyntheticTicket? revised = await ReviseGeneratedTicketAsync(
                session, current, audit, feedback, timings, attempt + 1, maxRevisions, ct);
            if (revised is null)
            {
                string failedLine = Truncate(
                    $"[{current.Category}] REINTERPRET failed · keeping prior scenario", 400);
                PublishProgress(session, progress => progress with
                {
                    AuditFeedbackFeed = AppendAuditFeedLine(progress.AuditFeedbackFeed, failedLine),
                    LatestAuditFeedback = failedLine,
                    PathTone = "reeval",
                });
                return current;
            }

            current = revised;
        }

        return current;
    }

    /// <summary>
    /// Rebuilds the training-LLM prompt around its own self-critique objection. Publishes the reeval tone
    /// so the mesh shows amber while the module is working the feedback rather than looking stalled.
    /// </summary>
    private async Task<SyntheticTicket?> ReviseGeneratedTicketAsync(
        NeuralNetTrainingSession session,
        SyntheticTicket rejected,
        SyntheticEvaluatorResult audit,
        SyntheticGeneratorFeedbackBuffer feedback,
        TrainingSessionTimings timings,
        int attempt,
        int maxRevisions,
        CancellationToken ct)
    {
        string reinterpretLine = Truncate(
            $"[{rejected.Category}] REINTERPRET ({attempt}/{maxRevisions}): {audit.Feedback}", 400);
        PublishProgress(session, progress =>
        {
            List<string> auditFeed = AppendAuditFeedLine(progress.AuditFeedbackFeed, reinterpretLine);
            return WithReevalMesh(
                progress with
                {
                    Phase = $"Training LLM · reinterpreting · {attempt}/{maxRevisions}",
                    PathTone = "reeval",
                    LatestAuditFeedback = Truncate($"REINTERPRET: {audit.Feedback}", 280),
                    LatestTrainingLlmSummary = Truncate(
                        $"Reworking '{rejected.Category}' prompt to resolve: {audit.Feedback}", 280),
                    AuditFeedbackFeed = auditFeed,
                    CurrentEvaluationData = FormatEvaluationData(
                        progress.TicketsGenerated, rejected, rejected.Messages.FirstOrDefault()),
                    GeneratorHints = feedback.Hints.ToList(),
                },
                lightFullMesh: true);
        });

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        SyntheticTicket? revised = await GenerateSyntheticTicketAsync(
            session.Mode,
            timings,
            feedback.Hints,
            rejected.Category,
            audit.Feedback,
            ct);
        watch.Stop();
        timings.TrainingLlmScenarioMs += watch.ElapsedMilliseconds;
        if (revised is null)
            return null;

        string revisedLine = Truncate(
            $"[{revised.Category}] REINTERPRETED ({attempt}/{maxRevisions}): new scenario ready · re-evaluating",
            400);
        PublishProgress(session, progress =>
        {
            List<string> auditFeed = AppendAuditFeedLine(progress.AuditFeedbackFeed, revisedLine);
            return WithReevalMesh(
                progress with
                {
                    Phase = $"Training LLM · reinterpreted · {revised.Category}",
                    PathTone = "reeval",
                    LatestAuditFeedback = revisedLine,
                    LatestTrainingLlmSummary = Truncate(
                        $"Revision {attempt} applied for '{revised.Category}'.", 280),
                    AuditFeedbackFeed = auditFeed,
                    CurrentEvaluationData = FormatEvaluationData(
                        progress.TicketsGenerated, revised, revised.Messages.FirstOrDefault()),
                    GeneratorHints = feedback.Hints.ToList(),
                },
                lightFullMesh: true);
        });
        // Brief hold so the reinterpreted line is visible before the next critique paint.
        await Task.Delay(TimeSpan.FromMilliseconds(750), ct);
        return revised;
    }

    /// <summary>
    /// Amber reeval lighting: empty active indexes mean the mesh/graph lights the full topology
    /// for <c>pathTone=reeval</c> (see worker + NeuralNetGraph2D idle-path rules).
    /// </summary>
    private static NeuralNetTrainingLiveProgress WithReevalMesh(
        NeuralNetTrainingLiveProgress progress,
        bool lightFullMesh)
    {
        if (!lightFullMesh)
            return progress;

        return progress with
        {
            ActiveNodeIndexes = [],
            ActiveEdgeParameterIndexes = [],
            ActiveLayerIndex = null,
        };
    }

    private static List<string> AppendAuditFeedLine(IReadOnlyList<string> existing, string line)
    {
        List<string> auditFeed = existing.ToList();
        auditFeed.Add(line);
        if (auditFeed.Count > 64)
            auditFeed.RemoveRange(0, auditFeed.Count - 64);
        return auditFeed;
    }

    private async Task RunChatMonitoringRunsAsync(
        NeuralNetTrainingSession session,
        IReadOnlyList<(int TicketIndex, SyntheticTicket? Ticket)> tickets,
        TrainingSessionTimings timings,
        SyntheticGeneratorFeedbackBuffer feedback,
        CancellationToken ct)
    {
        List<ChatMonitoringNeuralModelRun> runs = await db.ChatMonitoringNeuralModelRuns
            .Where(x => x.SessionId == session.SessionId)
            .OrderBy(x => x.ChatMonitoringKind)
            .ToListAsync(ct);
        using SemaphoreSlim persistenceGate = new(1, 1);
        if (TrainingHeapPressure.ShouldSkipTraces())
        {
            foreach (ChatMonitoringNeuralModelRun run in runs)
            {
                await RunChatMonitoringModelAsync(
                    session, run, tickets, persistenceGate, timings, feedback, ct);
            }
        }
        else
        {
            await Task.WhenAll(runs.Select(run =>
                RunChatMonitoringModelAsync(session, run, tickets, persistenceGate, timings, feedback, ct)));
        }
    }

    private async Task CompleteSyntheticSessionAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        session.Status = "Completed";
        session.CompletedAtUtc = DateTime.UtcNow;
        session.ReportJson = SerializeTrainingReport(timings);
        logger.LogInformation(
            "Synthetic training session {SessionId} completed. trainingLlm={TrainingLlm}ms labels={Labels}ms audits={Audits}ms train={Train}ms db={Db}ms vectors={Vectors}ms examples={Examples} audits={AuditCount}",
            session.SessionId,
            timings.TrainingLlmScenarioMs,
            timings.TeacherLabelMs,
            timings.AuditMs,
            timings.TrainMs,
            timings.DbSaveMs,
            timings.VectorUpsertMs,
            timings.ExamplesPersisted,
            timings.AuditCount);
        await db.SaveChangesAsync(ct);
    }

    private async Task CancelSyntheticSessionAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings)
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
        PublishProgress(session, TrainingHeapSpill.BoundAfterCancel);
    }

    private async Task FailSyntheticSessionAsync(
        NeuralNetTrainingSession session,
        TrainingSessionTimings timings,
        Exception ex)
    {
        session.Status = "Failed";
        session.CompletedAtUtc = DateTime.UtcNow;
        session.FailureReason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
        session.ReportJson = SerializeTrainingReport(timings);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private string SerializeTrainingReport(TrainingSessionTimings timings)
    {
        try
        {
            return JsonSerializer.Serialize(timings.ToReport(), JsonOptions);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Failed to serialize neural-net training report.");
            return """{"error":"report-serialization-failed"}""";
        }
        catch (JsonException ex)
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
        SyntheticGeneratorFeedbackBuffer feedback,
        CancellationToken ct)
    {
        IChatMonitoringNeuralModelTelemetry telemetry = ResolveChatMonitoringTelemetry(run);
        RestoreSpillCheckpoint(run, telemetry);
        ReplayBuilder replay = new(session, telemetry);
        run.Status = "Running";
        run.StartedAtUtc = DateTime.UtcNow;
        ChatMonitoringNeuralModelStateSnapshot topologyState = telemetry.GetStateSnapshot();
        PublishProgress(session, progress => progress with
        {
            Phase = $"Training {run.ChatMonitoringKind}",
            ActiveChatMonitoringKind = run.ChatMonitoringKind.ToString(),
            PathTone = "forward",
            LayerWidths = topologyState.LayerWidths,
            LayerLabels = topologyState.LayerLabels,
        });

        PersistenceBatch batch = new(db, vectors, persistenceGate, Options.PersistenceBatchSize, timings);
        List<PendingTrainItem> pendingTrain = [];
        ChatMonitoringRunContext runContext = new(
            session, run, telemetry, replay, batch, pendingTrain, timings, feedback);
        try
        {
            await OperationalExceptionGuard.RunObservingAsync(
                async () =>
                {
                    IReadOnlyList<(int TicketIndex, SyntheticTicket Ticket)> selected =
                        SelectTicketsForRun(
                            session.SessionId,
                            run.ChatMonitoringKind,
                            tickets,
                            Options.CrossDomainSampleRate);
                    int miniBatchSize = Math.Clamp(Options.MiniBatchSize, 1, 64);
                    foreach ((int ticketIndex, SyntheticTicket generated) in selected)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (TrainingHeapPressure.ShouldAttemptSpill())
                        {
                            bool spilled = await TrySpillTrainingHeapAsync(
                                session,
                                [runContext],
                                persistenceGate,
                                timings,
                                ticketIndex,
                                ct);
                            if (TrainingHeapSpill.AfterProactiveAttempt(spilled) == TrainingStepAfterSpill.Stop)
                                throw new TrainingHeapSpillFailedException(
                                    "Training heap spill failed; stopping the finite run.");
                        }

                        bool ticketCompleted = false;
                        try
                        {
                            await ProcessSyntheticTicketAsync(
                                runContext, ticketIndex, generated, miniBatchSize, ct);
                            ticketCompleted = true;
                        }
                        catch (OutOfMemoryException)
                        {
                            bool spilled = await TrySpillTrainingHeapAsync(
                                session,
                                [runContext],
                                persistenceGate,
                                timings,
                                ticketIndex,
                                CancellationToken.None);
                            if (TrainingHeapSpill.AfterOutOfMemory(spilled) == TrainingStepAfterSpill.Stop)
                                throw;
                            await Task.Delay(TimeSpan.FromSeconds(2), ct);
                        }

                        if (ticketCompleted)
                        {
                            PublishProgress(session, progress => progress with
                            {
                                Phase = $"Training {run.ChatMonitoringKind}",
                                ActiveChatMonitoringKind = run.ChatMonitoringKind.ToString(),
                                TicketsProcessed = progress.TicketsProcessed + 1,
                                MessagesProcessed = progress.MessagesProcessed + generated.Messages.Count,
                            });
                        }
                    }

                    await FlushPendingTrainingAsync(runContext, ct);
                    await CompleteChatMonitoringRunAsync(runContext, ct);
                },
                ex => FailChatMonitoringRunAsync(runContext, ex));
        }
        finally
        {
            await PersistAsync(persistenceGate, timings, CancellationToken.None);
        }
    }

    private IChatMonitoringNeuralModelTelemetry ResolveChatMonitoringTelemetry(ChatMonitoringNeuralModelRun run) =>
        chatMonitoringModels.Get(run.ChatMonitoringKind) as IChatMonitoringNeuralModelTelemetry
        ?? throw new InvalidOperationException("The configured chat-monitoring model does not support replay telemetry.");

    private async Task ProcessSyntheticTicketAsync(
        ChatMonitoringRunContext runContext,
        int ticketIndex,
        SyntheticTicket generated,
        int miniBatchSize,
        CancellationToken ct)
    {
        runContext.Replay.BeginTicket(ticketIndex, generated);
        foreach (SyntheticThreadMessage message in generated.Messages)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessSyntheticMessageAsync(runContext, ticketIndex, generated, message, miniBatchSize, ct);
        }
    }

    private async Task ProcessSyntheticMessageAsync(
        ChatMonitoringRunContext runContext,
        int ticketIndex,
        SyntheticTicket generated,
        SyntheticThreadMessage message,
        int miniBatchSize,
        CancellationToken ct)
    {
        PublishProgress(runContext.Session, progress => progress with
        {
            Phase = $"Evaluating · {runContext.Run.ChatMonitoringKind}",
            ActiveChatMonitoringKind = runContext.Run.ChatMonitoringKind.ToString(),
            CurrentEvaluationData = FormatEvaluationData(ticketIndex, generated, message),
            PathTone = "forward",
        });

        SyntheticMessageTrainingContext messageContext =
            BuildSyntheticMessageTrainingContext(runContext, ticketIndex, generated, message);
        ChatMonitoringNeuralModelPrediction prediction = messageContext.InitialInference.Prediction;
        SyntheticEvaluatorResult evaluation = messageContext.ResolvedEvaluation.Evaluation;

        if (IsWithinTolerance(prediction, evaluation))
        {
            await AddAcceptedSyntheticPassAsync(runContext, messageContext, ct);
            return;
        }

        QueueSyntheticTrainingExample(runContext, messageContext);
        if (runContext.PendingTrain.Count >= miniBatchSize)
            await FlushPendingTrainingAsync(runContext, ct);
    }

    private SyntheticMessageTrainingContext BuildSyntheticMessageTrainingContext(
        ChatMonitoringRunContext runContext,
        int ticketIndex,
        SyntheticTicket generated,
        SyntheticThreadMessage message)
    {
        string requirement = BuildSyntheticMessageRequirement(generated, message);
        SubjectSignalSnapshot subjectSignals = ResolveSyntheticSubjectSignals(generated, message, runContext.Run.ChatMonitoringKind);
        ChatMonitoringNeuralModelInput input = ChatMonitoringNeuralModelInput.Create(
            requirement,
            generated.ContextSnapshot,
            message.Content,
            communityVote: 0,
            threadContinuity: message.MessageIndex / 8f,
            priorScore: 0,
            subjectSignals);
        ChatMonitoringNeuralModelInferenceTrace initialInference = runContext.Telemetry.PredictWithTrace(input);
        SyntheticResolvedEvaluation resolvedEvaluation = ResolveCommunityAdjustedEvaluation(
            runContext.Session.SessionId,
            ticketIndex,
            generated,
            message,
            initialInference.Prediction,
            runContext.Run.ChatMonitoringKind,
            subjectSignals);

        return new SyntheticMessageTrainingContext(
            ticketIndex,
            generated,
            message,
            requirement,
            input,
            initialInference,
            resolvedEvaluation);
    }

    private static string BuildSyntheticMessageRequirement(
        SyntheticTicket generated,
        SyntheticThreadMessage message) =>
        $"{generated.Requirement}\nChannel: {message.Channel}\nAuthor role: {message.AuthorRole}";

    private static SubjectSignalSnapshot ResolveSyntheticSubjectSignals(
        SyntheticTicket generated,
        SyntheticThreadMessage message,
        NeuralModelKindChatMonitoring chatMonitoringKind) =>
        chatMonitoringKind switch
        {
            NeuralModelKindChatMonitoring.Tutoring => ChatMonitoringSubjectSignals.ResolveFromSynthetic(
                generated.Category,
                generated.Requirement,
                message.Channel,
                message.ChannelRelevance),
            _ => ChatMonitoringSubjectSignals.Resolve(
                [],
                ChatMonitoringSubjectSignals.ResolveChannelSubject(message.Channel),
                message.ChannelRelevance),
        };

    private static SyntheticResolvedEvaluation ResolveCommunityAdjustedEvaluation(
        Guid sessionId,
        int ticketIndex,
        SyntheticTicket generated,
        SyntheticThreadMessage message,
        ChatMonitoringNeuralModelPrediction prediction,
        NeuralModelKindChatMonitoring chatMonitoringKind,
        SubjectSignalSnapshot subjectSignals)
    {
        SyntheticEvaluatorResult evaluation = ResolveTeacherEvaluation(
            generated,
            message,
            prediction,
            chatMonitoringKind);
        int seed = HashCode.Combine(
            sessionId,
            ticketIndex,
            message.MessageIndex,
            1,
            (int)chatMonitoringKind);
        SyntheticCommunityResolution community = SyntheticCommunitySignalResolver.Resolve(
            message.CommunityIntent,
            (float)evaluation.ApprovalEstimate,
            (float)evaluation.EvaluatorConfidence,
            (float)evaluation.TargetScore,
            subjectSignals.EffectiveChannelRelevance,
            seed);
        evaluation = evaluation with { TargetScore = community.ResolvedEvidence };
        if (chatMonitoringKind == NeuralModelKindChatMonitoring.Tutoring)
            evaluation = evaluation with { TargetRelevance = Math.Clamp(evaluation.TargetRelevance * subjectSignals.RewardScale, 0, 1) };

        return new SyntheticResolvedEvaluation(evaluation, community);
    }

    private bool IsWithinTolerance(
        ChatMonitoringNeuralModelPrediction prediction,
        SyntheticEvaluatorResult evaluation) =>
        Math.Abs(prediction.Evidence - evaluation.TargetScore) <= Options.EvidenceTolerance
        && Math.Abs(prediction.Relevance - evaluation.TargetRelevance) <= Options.RelevanceTolerance;

    private async Task AddAcceptedSyntheticPassAsync(
        ChatMonitoringRunContext runContext,
        SyntheticMessageTrainingContext messageContext,
        CancellationToken ct)
    {
        SyntheticEvaluatorResult evaluation = messageContext.ResolvedEvaluation.Evaluation;
        if (ShouldAudit(
                runContext.Session.SessionId,
                messageContext.TicketIndex,
                messageContext.Message.MessageIndex,
                runContext.Run.ChatMonitoringKind))
        {
            evaluation = await MaybeAuditAsync(
                runContext.Session,
                messageContext.Ticket,
                messageContext.Message,
                messageContext.Requirement,
                messageContext.InitialInference.Prediction,
                evaluation,
                runContext.Feedback,
                runContext.Timings,
                ct);
        }

        runContext.Replay.AddPass(
            messageContext.TicketIndex,
            messageContext.Message,
            1,
            messageContext.Ticket,
            messageContext.InitialInference,
            evaluation,
            messageContext.ResolvedEvaluation.Community,
            null,
            true);
        if (!TrainingHeapPressure.ShouldSkipTraces())
        {
            PublishLayerWalk(
                runContext.Session,
                runContext,
                "Forward · accepted",
                "accepted",
                messageContext.InitialInference.Forward,
                backward: null);
        }
    }

    private void QueueSyntheticTrainingExample(
        ChatMonitoringRunContext runContext,
        SyntheticMessageTrainingContext messageContext)
    {
        SyntheticCommunityResolution community = messageContext.ResolvedEvaluation.Community;
        ChatMonitoringNeuralModelPrediction prediction = messageContext.InitialInference.Prediction;
        SyntheticEvaluatorResult evaluation = messageContext.ResolvedEvaluation.Evaluation;
        float signedVote = CalculateSignedVote(community);
        ChatMonitoringNeuralModelInput trainingInput = messageContext.Input with
        {
            CommunityVote = signedVote,
            PriorScore = prediction.Evidence,
        };
        int categoryIndex = ChatMonitoringTicketContext.CategoryIndex(
            messageContext.Ticket.Category,
            runContext.Run.ChatMonitoringKind);
        // The teacher's soft label when it supplied one, otherwise the hard category. Unrecognised
        // slugs are dropped by the taxonomy rather than folded into the general bucket, so a
        // hallucinated category name costs its own weight and nothing else.
        float[]? categoryDistribution = ChatMonitoringCategoryTaxonomy.BuildDistribution(
            runContext.Run.ChatMonitoringKind,
            messageContext.Message.TeacherCategoryWeights);
        ChatMonitoringNeuralModelTargets targets = new(
            (float)evaluation.TargetScore,
            (float)evaluation.TargetRelevance,
            categoryIndex,
            categoryDistribution);
        ChatMonitoringNeuralModelTrainingExample trainingExample = new(
            trainingInput,
            targets,
            messageContext.Ticket.Category);

        runContext.PendingTrain.Add(new PendingTrainItem(
            messageContext.TicketIndex,
            messageContext.Message,
            messageContext.Ticket,
            messageContext.Requirement,
            trainingInput,
            trainingExample,
            messageContext.InitialInference,
            evaluation,
            community,
            ShouldCaptureFullTrace(
                runContext.Session.SessionId,
                messageContext.TicketIndex,
                messageContext.Message.MessageIndex,
                runContext.Run.ChatMonitoringKind)));
    }

    private static float CalculateSignedVote(SyntheticCommunityResolution community) =>
        community.Sampling.VoterCount switch
        {
            0 => 0,
            _ => ((float)community.Sampling.Upvotes - community.Sampling.Downvotes)
                 / community.Sampling.VoterCount
                 * community.VoteConfidence,
        };

    private async Task FlushPendingTrainingAsync(ChatMonitoringRunContext runContext, CancellationToken ct)
    {
        if (runContext.PendingTrain.Count == 0) return;

        await FlushTrainMiniBatchAsync(runContext, ct);
    }

    private async Task CompleteChatMonitoringRunAsync(ChatMonitoringRunContext runContext, CancellationToken ct)
    {
        await runContext.Batch.FlushAsync(ct);
        runContext.Run.Status = "Completed";
        runContext.Run.CompletedAtUtc = DateTime.UtcNow;
        int ticketsProcessed = progressStore.Get(runContext.Session.SessionId)?.TicketsProcessed ?? 0;
        bool wroteSpill = WriteSpillCheckpoint(runContext.Session, runContext, ticketsProcessed, releaseHeapFirst: true);
        if (!wroteSpill && !TrainingHeapSpill.ShouldKeepSpillCheckpoint(runContext.Run.WorkerReplayJson))
        {
            string? completedJson = TryBuildReplayJson(
                runContext,
                ReplayCompletionStatus.Completed,
                failure: null);
            if (completedJson is not null)
                runContext.Run.WorkerReplayJson = completedJson;
            else
                logger.LogWarning(
                    "Skipped completed replay serialize for run {Kind} (insufficient memory).",
                    runContext.Run.ChatMonitoringKind);
        }

        runContext.Replay.ReleaseAccumulatedHeap();
    }

    private async Task FailChatMonitoringRunAsync(ChatMonitoringRunContext runContext, Exception ex)
    {
        // Best-effort drains only; preserve the original training FailureReason below.
        if (runContext.PendingTrain.Count > 0)
        {
            await OperationalExceptionGuard.RunAsync(
                () => FlushPendingTrainingAsync(runContext, CancellationToken.None),
                drainEx =>
                {
                    logger.LogWarning(drainEx, "Failed to flush pending chat-monitor training after run failure.");
                });
        }

        await OperationalExceptionGuard.RunAsync(
            () => runContext.Batch.FlushAsync(CancellationToken.None),
            drainEx =>
            {
                logger.LogWarning(drainEx, "Failed to flush chat-monitor persistence batch after run failure.");
            });

        runContext.Run.Status = "Failed";
        runContext.Run.CompletedAtUtc = DateTime.UtcNow;
        runContext.Run.FailureReason = Truncate(ex.Message, 1000);

        // Keep the original FailureReason; do not replace it with a secondary replay-build error.
        await OperationalExceptionGuard.RunAsync(
            () =>
            {
                bool wroteSpill = WriteSpillCheckpoint(
                    runContext.Session,
                    runContext,
                    progressStore.Get(runContext.Session.SessionId)?.TicketsProcessed ?? 0,
                    releaseHeapFirst: true);
                if (!wroteSpill && !TrainingHeapSpill.ShouldKeepSpillCheckpoint(runContext.Run.WorkerReplayJson))
                {
                    string? failedJson = TryBuildReplayJson(
                        runContext,
                        ReplayCompletionStatus.Failed,
                        new("training", "unhandled", Truncate(ex.Message, 1000)));
                    if (failedJson is not null)
                        runContext.Run.WorkerReplayJson = failedJson;
                }

                runContext.Replay.ReleaseAccumulatedHeap();
                return Task.CompletedTask;
            },
            replayEx =>
            {
                logger.LogWarning(replayEx, "Failed to serialize neural-net worker replay after training failure.");
            });
    }

    private async Task FlushTrainMiniBatchAsync(ChatMonitoringRunContext runContext, CancellationToken ct)
    {
        List<PendingTrainItem> pending = runContext.PendingTrain;
        if (pending.Count == 0) return;
        List<PendingTrainItem> items = [.. pending];
        pending.Clear();

        NeuralNetTrainingSession session = runContext.Session;
        ChatMonitoringNeuralModelRun run = runContext.Run;
        IChatMonitoringNeuralModelTelemetry telemetry = runContext.Telemetry;
        ReplayBuilder replay = runContext.Replay;
        PersistenceBatch batch = runContext.Batch;
        TrainingSessionTimings timings = runContext.Timings;

        int localEpochs = Math.Clamp(Options.LocalEpochs, 1, 100);
        if (session.MaxPassesPerTicket > 1)
            localEpochs = Math.Clamp(localEpochs * session.MaxPassesPerTicket / 3, 12, 100);
        NeuralTrainingTraceDetail detail = TrainingHeapPressure.ShouldSkipTraces()
            ? NeuralTrainingTraceDetail.None
            : items.Any(x => x.FullTrace)
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

        string lossSummary =
            $"CCEL/BCE avg cost {trainingTrace.FinalAverageCost:F4} · epochs {trainingTrace.Iterations.Count}";
        TrainingIterationReplay? lastIteration = trainingTrace.Iterations.LastOrDefault();
        List<string> weightFeed = BuildWeightUpdateFeed(
            telemetry.GetTopologySnapshot(),
            trainingTrace,
            lastIteration);
        PublishProgress(session, progress => progress with
        {
            Phase = $"Backprop · {run.ChatMonitoringKind}",
            ActiveChatMonitoringKind = run.ChatMonitoringKind.ToString(),
            ExamplesPersisted = progress.ExamplesPersisted + items.Count,
            LatestLossSummary = lossSummary,
            WeightUpdateFeed = weightFeed,
            CurrentEvaluationData = progress.CurrentEvaluationData,
        });
        if (!TrainingHeapPressure.ShouldSkipTraces())
        {
            PublishLayerWalk(
                session,
                runContext,
                $"Backprop · {run.ChatMonitoringKind}",
                "backprop",
                lastIteration?.AfterUpdate ?? lastIteration?.BeforeUpdate,
                lastIteration?.Backward);
        }

        foreach (PendingTrainItem item in items)
        {
            ChatMonitoringNeuralModelPrediction after = telemetry.Predict(item.TrainingInput);
            bool accepted = Math.Abs(after.Evidence - item.Evaluation.TargetScore) <= Options.EvidenceTolerance
                && Math.Abs(after.Relevance - item.Evaluation.TargetRelevance) <= Options.RelevanceTolerance;
            SyntheticEvaluatorResult evaluation = item.Evaluation;
            if (ShouldAudit(session.SessionId, item.TicketIndex, item.Message.MessageIndex, run.ChatMonitoringKind))
            {
                evaluation = await MaybeAuditAsync(
                    session,
                    item.Ticket,
                    item.Message,
                    item.Requirement,
                    item.InitialInference.Prediction,
                    evaluation,
                    runContext.Feedback,
                    timings,
                    ct);
            }

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

    /// <summary>
    /// Training-time second-pass audits are disabled: the multipurpose training LLM already embeds
    /// self-critique in the generation call, and a second Ollama round-trip doubled GPU/CPU cost.
    /// </summary>
    private Task<SyntheticEvaluatorResult> MaybeAuditAsync(
        NeuralNetTrainingSession session,
        SyntheticTicket generated,
        SyntheticThreadMessage message,
        string requirement,
        ChatMonitoringNeuralModelPrediction prediction,
        SyntheticEvaluatorResult evaluation,
        SyntheticGeneratorFeedbackBuffer feedback,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        _ = session;
        _ = generated;
        _ = message;
        _ = requirement;
        _ = prediction;
        _ = feedback;
        _ = timings;
        _ = ct;
        return Task.FromResult(evaluation);
    }

    private void PublishProgress(
        NeuralNetTrainingSession session,
        Func<NeuralNetTrainingLiveProgress, NeuralNetTrainingLiveProgress> update)
    {
        NeuralNetTrainingLiveProgress current = progressStore.Get(session.SessionId)
            ?? new NeuralNetTrainingLiveProgress(
                session.SessionId,
                session.Status,
                session.RequestedTicketCount,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                [],
                [],
                null,
                [],
                "idle",
                [],
                [],
                [],
                [],
                DateTime.UtcNow);
        progressStore.Upsert(update(current));
    }

    private static NeuralNetTrainingLiveProgress WithMeshFrame(
        NeuralNetTrainingLiveProgress progress,
        string pathTone,
        IChatMonitoringNeuralModelTelemetry telemetry,
        ForwardPropagationTrace? forward,
        BackpropagationTrace? backward,
        int? layerIndex = null)
    {
        IReadOnlyList<int> layerWidths = progress.LayerWidths;
        IReadOnlyList<string> layerLabels = progress.LayerLabels;
        if (layerWidths.Count == 0)
        {
            ChatMonitoringNeuralModelStateSnapshot state = telemetry.GetStateSnapshot();
            layerWidths = state.LayerWidths;
            layerLabels = state.LayerLabels;
        }

        (IReadOnlyList<int> activeNodes, IReadOnlyList<int> activeEdges) = layerIndex is int layer
            ? NeuralMeshFrameExtractor.ExtractLayer(forward, backward, layerWidths, layer)
            : NeuralMeshFrameExtractor.Extract(forward, backward);

        return progress with
        {
            PathTone = pathTone,
            LayerWidths = layerWidths,
            LayerLabels = layerLabels,
            ActiveNodeIndexes = activeNodes,
            ActiveEdgeParameterIndexes = activeEdges,
            ActiveLayerIndex = layerIndex,
        };
    }

    /// <summary>
    /// Publishes one live frame per layer transition so the mesh animates layer-by-layer instead of
    /// jumping from input to output. Backward walks run output-to-input to mirror gradient flow.
    /// </summary>
    private void PublishLayerWalk(
        NeuralNetTrainingSession session,
        ChatMonitoringRunContext runContext,
        string phase,
        string pathTone,
        ForwardPropagationTrace? forward,
        BackpropagationTrace? backward)
    {
        if (TrainingHeapPressure.ShouldSkipTraces() || (forward is null && backward is null))
        {
            PublishProgress(session, progress => progress with
            {
                Phase = phase,
                ActiveChatMonitoringKind = runContext.Run.ChatMonitoringKind.ToString(),
                PathTone = pathTone,
                ActiveNodeIndexes = [],
                ActiveEdgeParameterIndexes = [],
                ActiveLayerIndex = null,
            });
            return;
        }

        IReadOnlyList<int> layerWidths = progressStore.Get(session.SessionId)?.LayerWidths ?? [];
        if (layerWidths.Count == 0)
            layerWidths = runContext.Telemetry.GetStateSnapshot().LayerWidths;
        if (layerWidths.Count < 2)
        {
            PublishProgress(session, progress => WithMeshFrame(
                progress with { Phase = phase }, pathTone, runContext.Telemetry, forward, backward));
            return;
        }

        bool backwardWalk = backward is not null;
        IEnumerable<int> layerOrder = backwardWalk
            ? Enumerable.Range(1, layerWidths.Count - 1).Reverse()
            : Enumerable.Range(1, layerWidths.Count - 1);

        foreach (int layer in layerOrder)
        {
            string layerLabel = $"{phase} · layer {layer}/{layerWidths.Count - 1}";
            PublishProgress(session, progress => WithMeshFrame(
                progress with
                {
                    Phase = layerLabel,
                    ActiveChatMonitoringKind = runContext.Run.ChatMonitoringKind.ToString(),
                },
                pathTone,
                runContext.Telemetry,
                forward,
                backward,
                layer));
        }
    }

    private sealed record ChatMonitoringRunContext(
        NeuralNetTrainingSession Session,
        ChatMonitoringNeuralModelRun Run,
        IChatMonitoringNeuralModelTelemetry Telemetry,
        ReplayBuilder Replay,
        PersistenceBatch Batch,
        List<PendingTrainItem> PendingTrain,
        TrainingSessionTimings Timings,
        SyntheticGeneratorFeedbackBuffer Feedback);

    private sealed record SyntheticMessageTrainingContext(
        int TicketIndex,
        SyntheticTicket Ticket,
        SyntheticThreadMessage Message,
        string Requirement,
        ChatMonitoringNeuralModelInput Input,
        ChatMonitoringNeuralModelInferenceTrace InitialInference,
        SyntheticResolvedEvaluation ResolvedEvaluation);

    private sealed record SyntheticResolvedEvaluation(
        SyntheticEvaluatorResult Evaluation,
        SyntheticCommunityResolution Community);

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
            await DetachCancelledWritesIfSessionResumedAsync(ct);
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            await db.SaveChangesAsync(ct);
            watch.Stop();
            timings.AddDb(watch.ElapsedMilliseconds);
        }
        finally { persistenceGate.Release(); }
    }

    /// <summary>
    /// Weights-only mid-run persist: empty traces first, write
    /// <c>spill-checkpoint-v1</c>, then <see cref="PersistAsync"/>. Does not
    /// flush pending examples or vector upserts. The live net already holds
    /// the weights, so this path does not call <c>LoadParameterSnapshot</c>.
    /// </summary>
    private async Task<bool> TrySpillTrainingHeapAsync(
        NeuralNetTrainingSession session,
        IReadOnlyList<ChatMonitoringRunContext> contexts,
        SemaphoreSlim persistenceGate,
        TrainingSessionTimings timings,
        int ticketsProcessed,
        CancellationToken ct)
    {
        try
        {
            bool allWritten = true;
            foreach (ChatMonitoringRunContext runContext in contexts)
            {
                runContext.PendingTrain.Clear();
                TrainingHeapSpillPrepareResult prepared = TrainingHeapSpill.TryPrepare(
                    runContext.Replay.ReleaseAccumulatedHeap,
                    () => TryGetParameterSnapshot(runContext.Telemetry, out NeuralNetParameterSnapshot? snapshot)
                        ? snapshot
                        : null,
                    session.SessionId,
                    runContext.Run.ChatMonitoringKind,
                    ticketsProcessed);
                if (prepared.Succeeded && prepared.Json is not null && prepared.Snapshot is not null)
                {
                    runContext.Run.WorkerReplayJson = prepared.Json;
                    runContext.Replay.AdoptParameterSnapshot(prepared.Snapshot);
                }
                else
                {
                    allWritten = false;
                }
            }

            if (!allWritten)
            {
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                return false;
            }

            await PersistAsync(persistenceGate, timings, ct);
            TrainingHeapPressure.NoteSuccessfulSpill();
            PublishProgress(session, progress => TrainingHeapSpill.BoundAfterSpill(progress, ticketsProcessed));
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            return true;
        }
        catch (OutOfMemoryException ex)
        {
            logger.LogError(ex, "Failed to spill training heap for session {SessionId}.", session.SessionId);
            foreach (ChatMonitoringRunContext runContext in contexts)
                runContext.Replay.ReleaseAccumulatedHeap();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            return false;
        }
    }

    private bool WriteSpillCheckpoint(
        NeuralNetTrainingSession session,
        ChatMonitoringRunContext runContext,
        int ticketsProcessed,
        bool releaseHeapFirst)
    {
        Action release = releaseHeapFirst
            ? runContext.Replay.ReleaseAccumulatedHeap
            : static () => { };
        TrainingHeapSpillPrepareResult prepared = TrainingHeapSpill.TryPrepare(
            release,
            () => TryGetParameterSnapshot(runContext.Telemetry, out NeuralNetParameterSnapshot? snapshot)
                ? snapshot
                : null,
            session.SessionId,
            runContext.Run.ChatMonitoringKind,
            ticketsProcessed);
        if (!prepared.Succeeded || prepared.Json is null || prepared.Snapshot is null)
            return false;

        runContext.Run.WorkerReplayJson = prepared.Json;
        runContext.Replay.AdoptParameterSnapshot(prepared.Snapshot);
        return true;
    }

    private static void RestoreSpillCheckpoint(
        ChatMonitoringNeuralModelRun run,
        IChatMonitoringNeuralModelTelemetry telemetry) =>
        TrainingHeapSpill.TryRestore(run.WorkerReplayJson, telemetry.LoadParameterSnapshot);

    private string? TryBuildReplayJson(
        ChatMonitoringRunContext runContext,
        ReplayCompletionStatus status,
        ReplayFailure? failure)
    {
        try
        {
            return NeuralNetReplaySerializer.TrySerialize(
                runContext.Replay.Build(status, failure, Options.LocalEpochs));
        }
        catch (OutOfMemoryException ex)
        {
            logger.LogWarning(
                ex,
                "Replay serialize hit OutOfMemoryException for run {Kind}; keeping the spill checkpoint.",
                runContext.Run.ChatMonitoringKind);
            return null;
        }
    }

    private bool TryGetParameterSnapshot(
        IChatMonitoringNeuralModelTelemetry telemetry,
        out NeuralNetParameterSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            snapshot = telemetry.GetParameterSnapshot(null, 0);
            return true;
        }
        catch (OutOfMemoryException ex)
        {
            logger.LogWarning(ex, "Parameter snapshot allocation failed; spill checkpoint was not written.");
            return false;
        }
    }

    private static List<string> TrimFeed(IReadOnlyList<string>? feed) =>
        TrainingHeapSpill.TrimFeed(feed);

    private async Task DetachCancelledWritesIfSessionResumedAsync(CancellationToken ct)
    {
        HashSet<Guid> cancelledSessionIds = db.ChangeTracker.Entries<NeuralNetTrainingSession>()
            .Where(entry => entry.Entity.Status == "Cancelled")
            .Select(entry => entry.Entity.SessionId)
            .ToHashSet();
        if (cancelledSessionIds.Count == 0)
            return;

        HashSet<Guid> resumed = (await db.NeuralNetTrainingSessions
            .AsNoTracking()
            .Where(session => cancelledSessionIds.Contains(session.SessionId) && session.Status == "Queued")
            .Select(session => session.SessionId)
            .ToListAsync(ct)).ToHashSet();
        if (resumed.Count == 0)
            return;

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<NeuralNetTrainingSession> entry in db.ChangeTracker
            .Entries<NeuralNetTrainingSession>()
            .Where(tracked => resumed.Contains(tracked.Entity.SessionId))
            .ToList())
        {
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
        }

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ChatMonitoringNeuralModelRun> entry in db.ChangeTracker
            .Entries<ChatMonitoringNeuralModelRun>()
            .Where(tracked => resumed.Contains(tracked.Entity.SessionId))
            .ToList())
        {
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
        }
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
            "Fixed teacher label (LLM-1 scenario or deterministic fallback).", approval, confidence);
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

    private Task<SyntheticTicket?> GenerateSyntheticTicketAsync(
        NeuralTrainingMode mode,
        TrainingSessionTimings timings,
        IReadOnlyList<string>? generatorHints,
        string targetCategory,
        CancellationToken ct) =>
        GenerateSyntheticTicketAsync(mode, timings, generatorHints, targetCategory, revisionNotes: null, ct);

    private async Task<SyntheticTicket?> GenerateSyntheticTicketAsync(
        NeuralTrainingMode mode,
        TrainingSessionTimings timings,
        IReadOnlyList<string>? generatorHints,
        string targetCategory,
        string? revisionNotes,
        CancellationToken ct)
    {
        SyntheticThreadScenario? scenario = await trainingLlm.GenerateScenarioAsync(
            mode, generatorHints, targetCategory, revisionNotes, ct);
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
                labeled,
                scenario.SelfCritiqueVerdict,
                scenario.SelfCritiqueFeedback);
        }

        return await GenerateFallbackSyntheticTicketAsync(mode, targetCategory, ct);
    }

    private async Task<SyntheticTicket?> GenerateFallbackSyntheticTicketAsync(
        NeuralTrainingMode mode,
        string targetCategory,
        CancellationToken ct)
    {
        NeuralModelKindChatMonitoring kind =
            targetCategory.Contains("tutor", StringComparison.OrdinalIgnoreCase)
            || mode == NeuralTrainingMode.Tutoring
                ? NeuralModelKindChatMonitoring.Tutoring
                : NeuralModelKindChatMonitoring.Moderation;
        string normalizedTarget = ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, targetCategory);
        const string moderationFallbackPrompt =
            "Generate short fictional moderation-ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Scores are 0 to 1. Never include real personal data. Set category exactly to the requested concept slug.";
        const string tutoringFallbackPrompt =
            "Generate short fictional tutor-application ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Scores are 0 to 1. Never include real personal data. Set category exactly to the requested tutoring slug.";
        bool preferTutoring = kind == NeuralModelKindChatMonitoring.Tutoring;
        string systemPrompt = preferTutoring ? tutoringFallbackPrompt : moderationFallbackPrompt;
        string userPrompt = preferTutoring
            ? $"Create one school-chat tutor-application example. You MUST set category exactly to \"{normalizedTarget}\"."
            : $"Create one school-chat moderation example. You MUST set category exactly to \"{normalizedTarget}\" and mention reportedConcept={normalizedTarget} in requirement.";
        string? response = await llm.ChatJsonAsync(systemPrompt, userPrompt, ct);
        if (string.IsNullOrWhiteSpace(response))
        {
            SyntheticThreadScenario fixture = SyntheticThreadScenarioGenerator.CreateFallback(mode, normalizedTarget);
            SyntheticThreadMessage primary = fixture.Messages.First(message => !message.IsDistractor);
            return new SyntheticTicket(
                fixture.Category,
                fixture.Requirement,
                primary.Content,
                fixture.InitialContext,
                primary.TeacherEvidence ?? .5,
                primary.TeacherRelevance ?? primary.ChannelRelevance,
                fixture.Messages);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string category = GetString(root, "category");
            string requirement = GetString(root, "requirement");
            string message = GetString(root, "message");
            if (string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(requirement)
                || string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            category = ChatMonitoringCategoryTaxonomy.NormalizeCategory(kind, category);
            if (!string.Equals(category, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                category = normalizedTarget;
                if (kind == NeuralModelKindChatMonitoring.Moderation
                    && !requirement.Contains($"reportedConcept={normalizedTarget}", StringComparison.OrdinalIgnoreCase))
                {
                    requirement = $"Monitor reportedConcept={normalizedTarget}. {requirement}";
                }
            }

            float evidence = (float)GetUnit(root, "expectedScore", .5);
            float relevance = (float)GetUnit(root, "expectedRelevance", .5);
            SyntheticThreadMessage fallbackMessage = new(
                0,
                "synthetic-user",
                "student",
                "general",
                message[..Math.Min(4000, message.Length)],
                false,
                relevance,
                new(.5f, 10, .5f, []),
                evidence,
                relevance,
                evidence,
                .7f);
            return new(
                category[..Math.Min(80, category.Length)],
                requirement[..Math.Min(4000, requirement.Length)],
                message[..Math.Min(4000, message.Length)],
                Truncate(GetString(root, "contextSnapshot"), 2500),
                evidence,
                relevance,
                [fallbackMessage]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<SyntheticThreadMessage>> EnsureTeacherLabelsAsync(
        SyntheticThreadScenario scenario,
        TrainingSessionTimings timings,
        CancellationToken ct)
    {
        List<SyntheticThreadMessage> alreadyLabeled = scenario.Messages
            .Where(message => message.TeacherEvidence is not null && message.TeacherRelevance is not null)
            .ToList();
        List<SyntheticThreadMessage> needsLabel = scenario.Messages
            .Where(message => message.TeacherEvidence is null || message.TeacherRelevance is null)
            .ToList();

        if (needsLabel.Count == 0)
            return scenario.Messages;

        if (Options.PreferDeterministicTeacherLabels)
        {
            List<SyntheticThreadMessage> deterministic = needsLabel
                .Select(message => ApplyDeterministicTeacherLabel(scenario, message))
                .ToList();
            return MergeTeacherLabels(scenario.Messages, alreadyLabeled.Concat(deterministic));
        }

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        SyntheticThreadMessage?[] fromLlm = await Task.WhenAll(
            needsLabel.Select(message => LabelMessageTeacherAsync(scenario, message, ct)));
        watch.Stop();
        timings.AddTeacherLabel(watch.ElapsedMilliseconds);

        List<SyntheticThreadMessage> resolved = [];
        for (int index = 0; index < needsLabel.Count; index++)
        {
            resolved.Add(fromLlm[index] ?? ApplyDeterministicTeacherLabel(scenario, needsLabel[index]));
        }

        return MergeTeacherLabels(scenario.Messages, alreadyLabeled.Concat(resolved));
    }

    private static SyntheticThreadMessage ApplyDeterministicTeacherLabel(
        SyntheticThreadScenario scenario,
        SyntheticThreadMessage message)
    {
        SyntheticEvaluatorResult fallback = CreateFallbackEvaluation(
            new SyntheticTicket(
                scenario.Category,
                scenario.Requirement,
                message.Content,
                scenario.InitialContext,
                .5,
                message.ChannelRelevance,
                scenario.Messages),
            message,
            new ChatMonitoringNeuralModelPrediction(
                .5f,
                message.ChannelRelevance,
                .5f,
                NeuralModelKindChatMonitoring.Moderation,
                "label",
                "general",
                "fallback"));
        return message with
        {
            TeacherEvidence = (float)fallback.TargetScore,
            TeacherRelevance = (float)fallback.TargetRelevance,
            TeacherApprovalEstimate = (float)fallback.ApprovalEstimate,
            TeacherConfidence = (float)fallback.EvaluatorConfidence,
        };
    }

    private static IReadOnlyList<SyntheticThreadMessage> MergeTeacherLabels(
        IReadOnlyList<SyntheticThreadMessage> originalOrder,
        IEnumerable<SyntheticThreadMessage> labeled)
    {
        Dictionary<int, SyntheticThreadMessage> byIndex = labeled.ToDictionary(message => message.MessageIndex);
        return originalOrder
            .Select(message => byIndex.TryGetValue(message.MessageIndex, out SyntheticThreadMessage? labeledMessage)
                ? labeledMessage
                : message)
            .ToList();
    }

    private bool ShouldSampleGeneratorAudit(Guid sessionId, int ticketIndex)
    {
        double rate = Math.Clamp(Options.GeneratorAuditSampleRate, 0, 1);
        if (rate <= 0)
            return false;
        if (rate >= 1)
            return true;
        // Always critique the first ticket so self-revise steering has at least one signal.
        if (ticketIndex == 1)
            return true;
        int bucket = HashCode.Combine(sessionId, ticketIndex, 0x47415544);
        return (bucket & int.MaxValue) / (double)int.MaxValue < rate;
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
            "Deterministic teacher fallback used because LLM-1 labels were incomplete.", target, .65);
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

    private NeuralNetTrainingSessionDto MapSession(NeuralNetTrainingSession session, IEnumerable<ChatMonitoringNeuralModelRun>? runs = null)
    {
        NeuralNetTrainingLiveProgress? live = progressStore.Get(session.SessionId);
        return new()
        {
            SessionId = session.SessionId,
            RequestedTicketCount = session.RequestedTicketCount,
            MaxPassesPerTicket = session.MaxPassesPerTicket,
            Continuous = session.RequestedTicketCount == 0,
            Mode = session.Mode,
            Status = session.Status,
            CreatedAtUtc = session.CreatedAtUtc,
            StartedAtUtc = session.StartedAtUtc,
            CompletedAtUtc = session.CompletedAtUtc,
            FailureReason = session.FailureReason,
            HasReport = !string.IsNullOrWhiteSpace(session.ReportJson),
            ChatMonitoringRuns = (runs ?? []).OrderBy(x => x.ChatMonitoringKind).Select(x => new ChatMonitoringNeuralModelRunDto
            {
                ChatMonitoringKind = x.ChatMonitoringKind,
                Status = x.Status,
                CanonicalGeneration = x.CanonicalGeneration,
                HasWorkerReplay = !string.IsNullOrWhiteSpace(x.WorkerReplayJson),
                HasPromotionReplay = !string.IsNullOrWhiteSpace(x.PromotionReplayJson),
                FailureReason = x.FailureReason,
            }).ToList(),
            LiveProgress = MapLiveProgress(live),
        };
    }

    private NeuralNetTrainingSessionDto MapSessionSummary(SessionSummary session, IEnumerable<RunSummary> runs) => new()
    {
        SessionId = session.SessionId,
        RequestedTicketCount = session.RequestedTicketCount,
        MaxPassesPerTicket = session.MaxPassesPerTicket,
        Continuous = session.RequestedTicketCount == 0,
        Mode = session.Mode,
        Status = session.Status,
        CreatedAtUtc = session.CreatedAtUtc,
        StartedAtUtc = session.StartedAtUtc,
        CompletedAtUtc = session.CompletedAtUtc,
        FailureReason = session.FailureReason,
        HasReport = session.HasReport,
        ChatMonitoringRuns = runs.OrderBy(x => x.ChatMonitoringKind).Select(x => new ChatMonitoringNeuralModelRunDto
        {
            ChatMonitoringKind = x.ChatMonitoringKind,
            Status = x.Status,
            CanonicalGeneration = x.CanonicalGeneration,
            HasWorkerReplay = x.HasWorkerReplay,
            HasPromotionReplay = x.HasPromotionReplay,
            FailureReason = x.FailureReason,
        }).ToList(),
        LiveProgress = MapLiveProgress(progressStore.Get(session.SessionId)),
    };

    private static NeuralNetTrainingLiveProgressDto? MapLiveProgress(NeuralNetTrainingLiveProgress? live) =>
        live is null
            ? null
            : new NeuralNetTrainingLiveProgressDto
            {
                SessionId = live.SessionId,
                Phase = live.Phase,
                TicketsRequested = live.TicketsRequested,
                TicketsGenerated = live.TicketsGenerated,
                TicketsProcessed = live.TicketsProcessed,
                MessagesProcessed = live.MessagesProcessed,
                ExamplesPersisted = live.ExamplesPersisted,
                AuditsCompleted = live.AuditsCompleted,
                ActiveChatMonitoringKind = live.ActiveChatMonitoringKind,
                LatestTrainingLlmSummary = live.LatestTrainingLlmSummary,
                LatestAuditFeedback = live.LatestAuditFeedback,
                LatestLlm1Summary = live.LatestLlm1Summary,
                LatestLlm2Feedback = live.LatestLlm2Feedback,
                LatestLossSummary = live.LatestLossSummary,
                GeneratorHints = live.GeneratorHints,
                AuditFeedbackFeed = live.AuditFeedbackFeed,
                CurrentEvaluationData = live.CurrentEvaluationData,
                WeightUpdateFeed = live.WeightUpdateFeed,
                PathTone = live.PathTone,
                LayerWidths = live.LayerWidths,
                LayerLabels = live.LayerLabels,
                ActiveNodeIndexes = live.ActiveNodeIndexes,
                ActiveEdgeParameterIndexes = live.ActiveEdgeParameterIndexes,
                ActiveLayerIndex = live.ActiveLayerIndex,
                UpdatedAtUtc = live.UpdatedAtUtc,
            };

    private static string FormatEvaluationData(
        int ticketIndex,
        SyntheticTicket ticket,
        SyntheticThreadMessage? message)
    {
        string content = message?.Content ?? ticket.Message;
        string channel = message?.Channel ?? "—";
        string role = message?.AuthorRole ?? "—";
        return Truncate(
            $"Ticket #{ticketIndex} · {ticket.Category} · {channel}/{role} · target score {ticket.ExpectedScore:F2} · relevance {ticket.ExpectedRelevance:F2}\n{content}",
            900);
    }

    /// <summary>
    /// One line per target node currently receiving a non-zero Δw in the latest iteration,
    /// plus each updated parameter under that node.
    /// </summary>
    private static List<string> BuildWeightUpdateFeed(
        NeuralNetTopologySnapshot topology,
        TrainingPassTrace trainingTrace,
        TrainingIterationReplay? lastIteration)
    {
        if (lastIteration is null)
            return [];

        IReadOnlyList<ParameterDelta> deltas = lastIteration.Update.Parameters;
        List<string> lines =
        [
            $"Epoch {lastIteration.Epoch} · batch {trainingTrace.BatchSize} · cost {trainingTrace.FinalAverageCost:F4} · lr {lastIteration.Update.LearningRate:G4} · {deltas.Count} Δw",
        ];
        if (deltas.Count == 0)
        {
            lines.Add("No parameter deltas above epsilon in the latest mini-batch step.");
            return lines;
        }

        Dictionary<int, ReplayParameter> parametersByIndex = topology.Parameters
            .ToDictionary(parameter => parameter.Index);
        Dictionary<int, string> nodeLabels = topology.Nodes
            .ToDictionary(node => node.Index, node => node.Label);

        IOrderedEnumerable<IGrouping<int, ParameterDelta>> byTargetNode = deltas
            .GroupBy(delta =>
                parametersByIndex.TryGetValue(delta.ParameterIndex, out ReplayParameter? meta)
                    ? meta.TargetNodeIndex
                    : -1)
            .OrderBy(group => group.Key);

        foreach (IGrouping<int, ParameterDelta> group in byTargetNode)
        {
            string nodeLabel = group.Key >= 0 && nodeLabels.TryGetValue(group.Key, out string? label)
                ? label
                : $"node[{group.Key}]";
            float sumAbsDelta = group.Sum(delta => MathF.Abs(delta.Delta));
            lines.Add($"node[{group.Key}] {nodeLabel}: {group.Count()} weights · Σ|Δ|={sumAbsDelta:G4}");
            foreach (ParameterDelta delta in group.OrderByDescending(item => MathF.Abs(item.Delta)))
            {
                parametersByIndex.TryGetValue(delta.ParameterIndex, out ReplayParameter? meta);
                string edge = meta?.SourceNodeIndex is int source
                    ? $"w[{source}→{meta.TargetNodeIndex}]"
                    : $"b[{meta?.TargetNodeIndex ?? group.Key}]";
                lines.Add(
                    $"  {edge} #{delta.ParameterIndex}: {delta.ValueBefore:G4} → {delta.ValueAfter:G4} (Δ {delta.Delta:G4})");
            }
        }

        return TrainingHeapSpill.CapFeed(lines);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

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
        private NeuralNetParameterSnapshot initial;
        internal NeuralNetParameterSnapshot InitialSnapshot => initial;
        private long sequence;
        private int localRevision;

        /// <summary>Dense transitions in the stage-2 network; one replay frame is emitted per transition.</summary>
        private readonly int layerTransitions;

        public ReplayBuilder(NeuralNetTrainingSession session, IChatMonitoringNeuralModelTelemetry telemetry)
        {
            this.session = session; this.telemetry = telemetry;
            initial = telemetry.GetParameterSnapshot(0, 0);
            layerTransitions = Math.Max(1, telemetry.GetStateSnapshot().LayerWidths.Count - 1);
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
            inputs.Add(new(Intern(ticket.Requirement), Intern(ticket.ContextSnapshot), Intern(message.Content), Intern(message.Channel), Intern(message.AuthorRole), message.IsDistractor, message.ChannelRelevance, "training-llm", "synthetic-thread-v1"));
            Frame(ReplayPhase.Llm1Input, ReplayPayloadKind.Llm1Input, ticketIndex, passIndex, message.MessageIndex, null, inputIndex);
            int initialForward = AddForward(ReplayPhase.InitialForward, ticketIndex, passIndex, message.MessageIndex, null, initialInference.Forward);

            int evaluationIndex = evaluations.Count;
            evaluations.Add(new(true, accepted, accepted, (float)evaluation.TargetScore, (float)evaluation.TargetRelevance,
                (float)evaluation.TargetScore, (float)evaluation.ApprovalEstimate, (float)evaluation.EvaluatorConfidence, [], Intern(evaluation.Feedback), "training-llm", "self-critique-v1"));
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
                int back = backwards.Count; backwards.Add(iteration.Backward); AddLayerFrames(ReplayPhase.BackwardPropagation, ReplayPayloadKind.Backpropagation, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, back, forward: false);
                int update = updates.Count; updates.Add(iteration.Update); Frame(ReplayPhase.ParameterUpdate, ReplayPayloadKind.ParameterUpdate, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, update);
                AddForward(ReplayPhase.PostUpdateForward, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, iteration.AfterUpdate);
                int lossAfter = losses.Count; losses.Add(iteration.LossAfterUpdate); Frame(ReplayPhase.LossCalculation, ReplayPayloadKind.Loss, ticketIndex, passIndex, message.MessageIndex, iteration.Epoch, lossAfter);
                localRevision++;
            }
            int? finalForward = iterations.Count == 0 ? null : forwards.Count - 1;
            int verdict = verdicts.Count; verdicts.Add(new(accepted, Intern(accepted ? "Prediction within teacher-label / loss tolerance." : evaluation.Feedback), (float)evaluation.TargetScore, .75f, iterations.Count, initialForward, finalForward));
            Frame(ReplayPhase.FinalVerdict, ReplayPayloadKind.FinalVerdict, ticketIndex, passIndex, message.MessageIndex, null, verdict);
            // Omit per-pass dense snapshots; Build() already records initial/final parameters.
            messageState.Passes.Add(new(passIndex, message.MessageIndex, inputIndex, initialForward, evaluationIndex, generationIndex, voteEvaluationIndex, samplingIndex, iterations, finalForward, PostPassParameters: null));
        }

        /// <summary>
        /// After a spill, adopt the persisted snapshot so later compact replay
        /// matches the reloaded weights instead of the pre-restore singleton.
        /// </summary>
        public void AdoptParameterSnapshot(NeuralNetParameterSnapshot snapshot) =>
            initial = snapshot;

        /// <summary>
        /// Drops accumulated traces, frames, and interned strings after a DB spill or stop.
        /// Weights stay on the live model; <see cref="initial"/> is replaced via
        /// <see cref="AdoptParameterSnapshot"/> when a spill checkpoint is written.
        /// </summary>
        public void ReleaseAccumulatedHeap()
        {
            stringIndices.Clear();
            strings.Clear();
            tickets.Clear();
            frames.Clear();
            inputs.Clear();
            forwards.Clear();
            evaluations.Clear();
            losses.Clear();
            backwards.Clear();
            updates.Clear();
            verdicts.Clear();
            voteGeneration.Clear();
            voteEvaluation.Clear();
            voteSampling.Clear();
            sequence = 0;
        }

        public NeuralNetReplayReportV2 Build(ReplayCompletionStatus status, ReplayFailure? failure = null, int epochs = 12)
        {
            NeuralNetParameterSnapshot final = telemetry.GetParameterSnapshot(null, localRevision);
            IReadOnlyList<TrainingTicketReplay> ticketReplay = tickets.Select(ticket => new TrainingTicketReplay(ticket.Index, Intern(ticket.Ticket.Category), Intern(ticket.Ticket.Requirement), Intern(ticket.Ticket.ContextSnapshot), ticket.Messages.Select(message => new TrainingMessageReplay(message.Message.MessageIndex, Intern(message.Message.AuthorId), Intern(message.Message.AuthorRole), Intern(message.Message.Channel), message.Message.IsDistractor, message.Message.ChannelRelevance, message.Passes)).ToList())).ToList();
            ReplayPayloadCollections payloads = new(inputs, forwards, evaluations, losses, backwards, updates, verdicts, voteGeneration, voteEvaluation, voteSampling);
            TrainingProvenance provenance = new(telemetry.GetStateSnapshot().ModelVersion, "hashed-text-48-v1", "bce+categorical-cross-entropy-avg-v1", "momentum-mini-batch-SGD", .035f, epochs, "hc-xoshiro256ss-v1", 0x48434D4C, "replay-v2-worker-v1");
            ReplayIntegrity placeholder = new("hc-replay-canonical-json-v1", "sha-256", "", initial.Checksum, final.Checksum, "");
            NeuralNetReplayReportV2 draft = new("2.0", session.SessionId, status, telemetry.GetTopologySnapshot(), new(strings), provenance, initial, ticketReplay, frames, payloads, final, placeholder, failure);
            string? draftJson = NeuralNetReplaySerializer.TrySerialize(draft);
            if (draftJson is null)
                throw new InvalidOperationException("Replay report exceeded process memory while serializing.");
            ReplayIntegrity integrity = NeuralNetReplaySerializer.CreateIntegrity(draft.Topology, initial, final, draftJson);
            NeuralNetReplayReportV2 result = draft with { Integrity = integrity };
            NeuralNetReplaySerializer.Validate(result);
            return result;
        }

        private int Intern(string value) { if (stringIndices.TryGetValue(value, out int existing)) return existing; int index = strings.Count; strings.Add(value); stringIndices[value] = index; return index; }

        private int AddForward(ReplayPhase phase, int ticket, int pass, int message, int? epoch, ForwardPropagationTrace forward)
        {
            int index = forwards.Count;
            forwards.Add(forward);
            AddLayerFrames(phase, ReplayPayloadKind.Forward, ticket, pass, message, epoch, index, forward: true);
            return index;
        }

        /// <summary>
        /// Emits one frame per dense transition against a single shared payload so stepping the
        /// replay advances a layer at a time. Forward phases run input-to-output, backward phases
        /// output-to-input.
        /// </summary>
        private void AddLayerFrames(ReplayPhase phase, ReplayPayloadKind kind, int ticket, int pass, int? message, int? epoch, int payload, bool forward)
        {
            IEnumerable<int> layers = Enumerable.Range(1, layerTransitions);
            foreach (int layer in forward ? layers : layers.Reverse())
                Frame(phase, kind, ticket, pass, message, epoch, payload, layer);
        }

        private void Frame(ReplayPhase phase, ReplayPayloadKind kind, int ticket, int pass, int? message, int? epoch, int payload, int? layer = null)
        {
            frames.Add(new(++sequence, phase, kind, ticket, pass, message, epoch, DateTimeOffset.UtcNow, payload, layer));

            // Layer walking multiplies frame counts, and continuous sessions never stop producing
            // them. Keep the newest window so a long run stays under the V2 import limit instead of
            // failing validation when the replay is serialized.
            if (frames.Count > NeuralNetReplaySerializer.MaxFrames)
                frames.RemoveRange(0, frames.Count - NeuralNetReplaySerializer.MaxFrames);
        }
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
        private readonly List<(string Content, string PositionId, Guid CanonicalId, object Metadata)> pendingVectors = [];

        public async Task EnqueueAsync(TicketModelTrainingExample record, string content, string positionId, CancellationToken ct)
        {
            examples.Add(record);
            // Embeddings are computed on flush so a whole batch can hash in parallel.
            pendingVectors.Add((content, positionId, record.TrainingExampleId,
                new { record.TrainingExampleId, record.Category, record.TargetScore, record.TargetRelevance, record.Source, record.ChatMonitoringKind }));
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
                List<(string Content, string PositionId, Guid CanonicalId, object Metadata)> batch = [.. pendingVectors];
                IReadOnlyList<float>[] embeddings = new IReadOnlyList<float>[batch.Count];
                Parallel.For(0, batch.Count, index =>
                {
                    embeddings[index] = ChatMonitoringFeatureEncoder.EmbedText(batch[index].Content);
                });

                int nextIndex = 0;
                try
                {
                    for (; nextIndex < batch.Count; nextIndex++)
                    {
                        (string content, string positionId, Guid canonicalId, object metadata) = batch[nextIndex];
                        await vectors.UpsertAsync(
                            VectorNamespaces.TicketTrainingExample,
                            content,
                            embeddings[nextIndex],
                            positionId,
                            canonicalId,
                            metadata,
                            ct);
                    }

                    pendingVectors.Clear();
                }
                catch (Exception)
                {
                    // Keep only the unsent suffix so a later flush can retry.
                    pendingVectors.Clear();
                    pendingVectors.AddRange(batch.Skip(nextIndex));
                    throw;
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
        public long TrainingLlmScenarioMs;
        public long TeacherLabelMs;
        public long AuditMs;
        public long TrainMs;
        public long DbSaveMs;
        public long VectorUpsertMs;
        public int TrainingLlmJsonRetries;
        public int ExamplesPersisted;
        public int AuditCount;
        public double CostSum;
        public int CostSamples;

        public void AddTeacherLabel(long ms) { lock (gate) TeacherLabelMs += ms; }
        public void AddAudit(long ms) { lock (gate) { AuditMs += ms; AuditCount++; } }
        public void AddTrainingLlmRetry() { lock (gate) TrainingLlmJsonRetries++; }
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
            trainingLlmScenarioMs = TrainingLlmScenarioMs,
            teacherLabelMs = TeacherLabelMs,
            auditMs = AuditMs,
            trainMs = TrainMs,
            dbSaveMs = DbSaveMs,
            vectorUpsertMs = VectorUpsertMs,
            trainingLlmJsonRetries = TrainingLlmJsonRetries,
            examplesPersisted = ExamplesPersisted,
            auditCount = AuditCount,
            averageCost = CostSamples == 0 ? 0d : NeuralNetFinite.OrZero(CostSum / CostSamples),
            costSamples = CostSamples,
        };
    }
}
