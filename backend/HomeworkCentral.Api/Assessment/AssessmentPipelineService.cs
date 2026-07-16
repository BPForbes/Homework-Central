using System.Text.Json;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Assessment;

public interface IAssessmentPipelineService
{
    Task ProcessMessageAsync(AssessmentMessageJob job, CancellationToken ct = default);
}

public sealed class AssessmentPipelineService(
    AppDbContext db,
    ILlmClient llm,
    IVectorDocumentStore vectors,
    ICommunityScoreAggregator community,
    ICandidateStateService candidateState,
    ILogger<AssessmentPipelineService> logger) : IAssessmentPipelineService
{
    public async Task ProcessMessageAsync(AssessmentMessageJob job, CancellationToken ct = default)
    {
        List<TicketUserWatch> watches = await db.TicketUserWatches
            .Include(w => w.Ticket)
            .ThenInclude(t => t.Portal)
            .Where(w => w.IsActive
                        && w.TrackedUserId == job.SenderId
                        && w.Ticket.ClosedAtUtc == null
                        && !w.Ticket.AiTrackingOptOut
                        && w.Ticket.DecisionApprovedAtUtc == null)
            .ToListAsync(ct);

        if (watches.Count == 0)
            return;

        IReadOnlyList<float> embedding = await llm.EmbedAsync(job.Content, ct);
        await vectors.UpsertAsync(
            VectorNamespaces.CandidateEvidence,
            job.Content,
            embedding,
            positionId: null,
            canonicalRecordId: job.MessageId,
            metadata: new { messageId = job.MessageId, userId = job.SenderId, roomId = job.RoomId },
            ct);

        // Never retrieve running μ scores into the LLM prompt.
        IReadOnlyList<VectorDocument> rubricDocs = await vectors.RetrieveSimilarAsync(
            VectorNamespaces.ScoringReference,
            embedding,
            take: 6,
            ct: ct);

        foreach (TicketUserWatch watch in watches)
        {
            CandidateApplication? application = await db.CandidateApplications
                .FirstOrDefaultAsync(a => a.TicketId == watch.TicketId && !a.AiOptOut, ct);

            if (application is null)
            {
                if (!string.Equals(
                        watch.Ticket.FilterName,
                        DefaultTicketPortalPresets.TutorFilterName,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        watch.Ticket.FilterName,
                        DefaultTicketPortalPresets.ModFilterName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                application = new CandidateApplication
                {
                    CandidateApplicationId = Guid.NewGuid(),
                    UserId = watch.TrackedUserId,
                    PositionId = string.Equals(
                        watch.Ticket.FilterName,
                        DefaultTicketPortalPresets.ModFilterName,
                        StringComparison.OrdinalIgnoreCase)
                        ? "mod_report"
                        : "tutor",
                    Status = CandidateApplicationStatuses.InsufficientEvidence,
                    TicketId = watch.TicketId,
                    AiOptOut = false,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.CandidateApplications.Add(application);
                await db.SaveChangesAsync(ct);
            }

            string eligibilityJson = await llm.ChatJsonAsync(
                "You classify whether a chat message is eligible evidence for a school ticket assessment. "
                + "Respond JSON only: {\"eligible\":bool,\"subjects\":{string:number},\"difficulty\":number,"
                + "\"originalityConfidence\":number,\"relevance\":number}.",
                $"Position: {application.PositionId}\nFilter: {watch.Ticket.FilterName}\n"
                + $"Template: {watch.Ticket.TrackingTemplateJson}\n"
                + $"Rubric context:\n{string.Join("\n---\n", rubricDocs.Select(d => d.ContentText))}\n"
                + $"Message:\n{job.Content}",
                ct) ?? """{"eligible":false,"subjects":{},"difficulty":0,"originalityConfidence":0,"relevance":0}""";

            using JsonDocument eligibilityDoc = JsonDocument.Parse(eligibilityJson);
            JsonElement root = eligibilityDoc.RootElement;
            if (!root.TryGetProperty("eligible", out JsonElement eligibleEl) || !eligibleEl.GetBoolean())
                continue;

            Dictionary<string, double> subjects = [];
            if (root.TryGetProperty("subjects", out JsonElement subjectsEl)
                && subjectsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in subjectsEl.EnumerateObject())
                    subjects[prop.Name] = prop.Value.GetDouble();
            }

            double difficulty = root.TryGetProperty("difficulty", out JsonElement d) ? d.GetDouble() : 0.5;
            double originality = root.TryGetProperty("originalityConfidence", out JsonElement o)
                ? o.GetDouble()
                : 0.5;
            double relevance = root.TryGetProperty("relevance", out JsonElement r) ? r.GetDouble() : 0.5;

            string rubricJson = await llm.ChatJsonAsync(
                "You evaluate a tutoring/moderation evidence message. Respond JSON only with keys "
                + "correctness,reasoning,pedagogy,relevance,communication,professionalism,"
                + "evaluatorConfidence (0-1), criticalErrors (string[]), evidence (array of "
                + "{criterion,messageSpan,explanation}). Do not include any overall candidate score.",
                $"Position: {application.PositionId}\nMessage:\n{job.Content}\n"
                + $"Rubric snippets:\n{string.Join("\n---\n", rubricDocs.Select(doc => doc.ContentText))}",
                ct) ?? """{"correctness":0.5,"reasoning":0.5,"pedagogy":0.5,"relevance":0.5,"communication":0.5,"professionalism":0.5,"evaluatorConfidence":0.5,"criticalErrors":[],"evidence":[]}""";

            using JsonDocument rubricDoc = JsonDocument.Parse(rubricJson);
            JsonElement rub = rubricDoc.RootElement;
            RubricEvaluation evaluation = new(
                GetNum(rub, "correctness"),
                GetNum(rub, "reasoning"),
                GetNum(rub, "pedagogy"),
                GetNum(rub, "relevance"),
                GetNum(rub, "communication"),
                GetNum(rub, "professionalism"),
                GetNum(rub, "evaluatorConfidence"),
                rub.TryGetProperty("criticalErrors", out JsonElement errs) && errs.ValueKind == JsonValueKind.Array
                    ? errs.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                    : []);

            double q = DeterministicScoring.ResponseQuality(evaluation);
            double w = DeterministicScoring.EvidenceWeight(
                difficulty,
                relevance,
                evaluation.EvaluatorConfidence,
                originality);
            (double? g, double reliableWeight) = await community.AggregateAsync(job.MessageId, ct);
            double lambda = DeterministicScoring.CommunityLambda(reliableWeight);
            double s = DeterministicScoring.CombineScores(q, g, lambda);

            if (evaluation.CriticalErrors.Count > 0)
            {
                application.Status = CandidateApplicationStatuses.CriticalConcern;
                await db.SaveChangesAsync(ct);
            }

            AssessmentEvent assessmentEvent = new()
            {
                AssessmentEventId = Guid.NewGuid(),
                CandidateApplicationId = application.CandidateApplicationId,
                MessageId = job.MessageId,
                RubricVersion = "1.0",
                EvaluatorModelVersion = "llm",
                RawEvaluationJson = rubricJson,
                LlmScore = q,
                CommunityScore = g,
                CombinedScore = s,
                EvidenceWeight = w,
                Difficulty = difficulty,
                Relevance = relevance,
                Confidence = evaluation.EvaluatorConfidence,
                AuthenticityConfidence = originality,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.AssessmentEvents.Add(assessmentEvent);
            await db.SaveChangesAsync(ct);

            await candidateState.ApplyAssessmentEventAsync(
                application.CandidateApplicationId,
                assessmentEvent,
                subjects,
                ct);
            await candidateState.EvaluateDecisionStateAsync(application.CandidateApplicationId, ct);

            logger.LogInformation(
                "Assessment event {EventId} for application {ApplicationId} message {MessageId}: s={Score:F2} w={Weight:F2}",
                assessmentEvent.AssessmentEventId,
                application.CandidateApplicationId,
                job.MessageId,
                s,
                w);
        }
    }

    private static double GetNum(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : 0.5;
}
