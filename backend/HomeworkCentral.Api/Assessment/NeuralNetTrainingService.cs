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
}

public sealed class NeuralNetTrainingService(
    AppDbContext db,
    ITicketStudentModel student,
    IVectorDocumentStore vectors,
    ILlmClient llm,
    INeuralNetTrainingQueue queue) : INeuralNetTrainingService
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

    public async Task RunSyntheticSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        NeuralNetTrainingSession? session = await db.NeuralNetTrainingSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);
        if (session is null || session.Status != "Queued") return;

        session.Status = "Running";
        session.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        SyntheticTrainingReport report = new(session.SessionId, session.StartedAtUtc.Value, session.RequestedTicketCount, session.MaxPassesPerTicket, student.GetStateSnapshot());
        try
        {
            for (int ticketIndex = 1; ticketIndex <= session.RequestedTicketCount; ticketIndex++)
            {
                SyntheticTicket? generated = await GenerateSyntheticTicketAsync(ct);
                if (generated is null)
                {
                    report.Tickets.Add(new(ticketIndex, null, [], "Generator returned invalid JSON."));
                    continue;
                }

                SyntheticTicketReport ticket = new(ticketIndex, generated, []);
                report.Tickets.Add(ticket);
                for (int pass = 1; pass <= session.MaxPassesPerTicket; pass++)
                {
                    string modelMessage = ComposeModelMessage(generated.ContextSnapshot, generated.Message);
                    TicketStudentPrediction prediction = student.Predict(generated.Requirement, modelMessage);
                    NeuralNetStateSnapshot before = student.GetStateSnapshot();
                    SyntheticEvaluatorResult? evaluation = await EvaluateSyntheticTicketAsync(generated, prediction, ct);
                    if (evaluation is null)
                    {
                        ticket.Passes.Add(new(pass, prediction, null, before, before, false, "Evaluator returned invalid JSON."));
                        break;
                    }

                    bool lgtm = string.Equals(evaluation.Verdict, "LGTM", StringComparison.OrdinalIgnoreCase);
                    if (!lgtm)
                    {
                        StudentTrainingExample trainingExample = new(generated.Requirement, modelMessage,
                            evaluation.TargetScore, evaluation.TargetRelevance, generated.Category);
                        student.Train(trainingExample, epochs: 12);
                        TicketModelTrainingExample record = new()
                        {
                            TrainingExampleId = Guid.NewGuid(), Requirement = generated.Requirement,
                            BootstrapMessage = generated.Message, TargetScore = evaluation.TargetScore,
                            TargetRelevance = evaluation.TargetRelevance, Category = generated.Category,
                            Source = "SyntheticLlmTraining", ApprovedAtUtc = DateTime.UtcNow,
                            ApprovedByUserId = session.StartedByUserId,
                            ContextSnapshot = generated.ContextSnapshot,
                        };
                        db.TicketModelTrainingExamples.Add(record);
                        await db.SaveChangesAsync(ct);
                        await vectors.UpsertAsync(VectorNamespaces.TicketTrainingExample, generated.Message,
                            student.Embed(generated.Message), generated.Category, record.TrainingExampleId,
                            new { record.TrainingExampleId, record.Category, record.TargetScore, record.TargetRelevance, record.Source }, ct);
                    }
                    NeuralNetStateSnapshot after = student.GetStateSnapshot();
                    ticket.Passes.Add(new(pass, prediction, evaluation, before, after, lgtm, evaluation.Feedback));
                    if (lgtm) break;
                }
            }
            report.CompletedAtUtc = DateTime.UtcNow;
            report.FinalState = student.GetStateSnapshot();
            session.Status = "Completed";
            session.CompletedAtUtc = report.CompletedAtUtc;
            session.ReportJson = JsonSerializer.Serialize(report, JsonOptions);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.CompletedAtUtc = DateTime.UtcNow;
            report.FinalState = student.GetStateSnapshot();
            report.FailureReason = ex.Message;
            session.Status = "Failed";
            session.CompletedAtUtc = report.CompletedAtUtc;
            session.FailureReason = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            session.ReportJson = JsonSerializer.Serialize(report, JsonOptions);
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

    private async Task<SyntheticTicket?> GenerateSyntheticTicketAsync(CancellationToken ct)
    {
        const string systemPrompt = "Generate short fictional moderation-ticket examples only. Return JSON: category, requirement, message, contextSnapshot, expectedScore, expectedRelevance. Scores are 0 to 1. Never include real personal data.";
        string? response = await llm.ChatJsonAsync(systemPrompt, "Create one varied school-chat moderation example.", ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            string category = GetString(root, "category"), requirement = GetString(root, "requirement"), message = GetString(root, "message");
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(requirement) || string.IsNullOrWhiteSpace(message)) return null;
            return new(category[..Math.Min(80, category.Length)], requirement[..Math.Min(4000, requirement.Length)], message[..Math.Min(4000, message.Length)], Truncate(GetString(root, "contextSnapshot"), 2500), GetUnit(root, "expectedScore", .5), GetUnit(root, "expectedRelevance", .5));
        }
        catch (JsonException) { return null; }
    }

    private async Task<SyntheticEvaluatorResult?> EvaluateSyntheticTicketAsync(SyntheticTicket ticket, TicketStudentPrediction prediction, CancellationToken ct)
    {
        const string systemPrompt = "You are a teacher for a small moderation classifier. Return JSON only: verdict (LGTM or REVISE), targetScore (0..1), targetRelevance (0..1), feedback. Use LGTM only if the student classification is sufficiently correct.";
        string prompt = $"<requirement>{ticket.Requirement}</requirement>\n<context>{ticket.ContextSnapshot}</context>\n<message>{ticket.Message}</message>\n<student_score>{prediction.EvidenceScore:F3}</student_score>\n<student_relevance>{prediction.Relevance:F3}</student_relevance>\n<student_confidence>{prediction.Confidence:F3}</student_confidence>";
        string? response = await llm.ChatJsonAsync(systemPrompt, prompt, ct);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            string verdict = GetString(root, "verdict");
            if (!string.Equals(verdict, "LGTM", StringComparison.OrdinalIgnoreCase) && !string.Equals(verdict, "REVISE", StringComparison.OrdinalIgnoreCase)) return null;
            return new(verdict.ToUpperInvariant(), GetUnit(root, "targetScore", prediction.EvidenceScore), GetUnit(root, "targetRelevance", prediction.Relevance), Truncate(GetString(root, "feedback"), 2000));
        }
        catch (JsonException) { return null; }
    }

    private static string GetString(JsonElement root, string property) => root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double GetUnit(JsonElement root, string property, double fallback) => root.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? Math.Clamp(result, 0, 1) : fallback;
    private static NeuralNetTrainingSessionDto MapSession(NeuralNetTrainingSession session) => new()
    {
        SessionId = session.SessionId, RequestedTicketCount = session.RequestedTicketCount, MaxPassesPerTicket = session.MaxPassesPerTicket, Status = session.Status,
        CreatedAtUtc = session.CreatedAtUtc, StartedAtUtc = session.StartedAtUtc, CompletedAtUtc = session.CompletedAtUtc, FailureReason = session.FailureReason, HasReport = !string.IsNullOrWhiteSpace(session.ReportJson),
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record SyntheticTicket(string Category, string Requirement, string Message, string ContextSnapshot, double ExpectedScore, double ExpectedRelevance);
    private sealed record SyntheticEvaluatorResult(string Verdict, double TargetScore, double TargetRelevance, string Feedback);
    private sealed record SyntheticPassReport(int Pass, TicketStudentPrediction Prediction, SyntheticEvaluatorResult? Evaluation, NeuralNetStateSnapshot BeforeState, NeuralNetStateSnapshot AfterState, bool Lgtm, string Feedback);
    private sealed record SyntheticTicketReport(int TicketIndex, SyntheticTicket? Ticket, List<SyntheticPassReport> Passes, string? GeneratorFailure = null);
    private sealed record SyntheticTrainingReport(Guid SessionId, DateTime StartedAtUtc, int RequestedTicketCount, int MaxPassesPerTicket, NeuralNetStateSnapshot InitialState)
    {
        public List<SyntheticTicketReport> Tickets { get; } = [];
        public DateTime? CompletedAtUtc { get; set; }
        public NeuralNetStateSnapshot? FinalState { get; set; }
        public string? FailureReason { get; set; }
    }
}
