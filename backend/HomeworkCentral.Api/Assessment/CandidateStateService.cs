using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Assessment;

public interface ICandidateStateService
{
    Task ApplyAssessmentEventAsync(
        Guid applicationId,
        AssessmentEvent assessmentEvent,
        IReadOnlyDictionary<string, double> subjectMemberships,
        CancellationToken ct = default);

    /// <summary>
    /// Adjusts competency Beta parameters when an existing message's combined score changes
    /// (e.g. community vote recalc) without re-applying full evidence volume.
    /// </summary>
    Task ApplyCombinedScoreDeltaAsync(
        Guid applicationId,
        AssessmentEvent baselineEvent,
        AssessmentEvent adjustmentEvent,
        CancellationToken ct = default);

    Task<string> EvaluateDecisionStateAsync(Guid applicationId, CancellationToken ct = default);
}

public sealed class CandidateStateService(AppDbContext db) : ICandidateStateService
{
    public async Task ApplyAssessmentEventAsync(
        Guid applicationId,
        AssessmentEvent assessmentEvent,
        IReadOnlyDictionary<string, double> subjectMemberships,
        CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        foreach ((string competencyId, double z) in subjectMemberships)
        {
            if (z <= 0)
                continue;

            CandidateCompetencyState? state = await db.CandidateCompetencyStates
                .FirstOrDefaultAsync(
                    s => s.CandidateApplicationId == applicationId && s.CompetencyId == competencyId,
                    ct);

            if (state is null)
            {
                state = new CandidateCompetencyState
                {
                    CandidateApplicationId = applicationId,
                    CompetencyId = competencyId,
                    Alpha = 1,
                    Beta = 1,
                    LastUpdatedAtUtc = now,
                };
                db.CandidateCompetencyStates.Add(state);
            }

            double wIk = assessmentEvent.EvidenceWeight * z;
            double alpha = state.Alpha;
            double beta = state.Beta;
            DeterministicScoring.UpdateBeta(ref alpha, ref beta, wIk, assessmentEvent.CombinedScore);
            state.Alpha = alpha;
            state.Beta = beta;
            state.MeanScore = DeterministicScoring.Mean(alpha, beta);
            state.EvidenceVolume = DeterministicScoring.EvidenceVolume(alpha, beta);
            state.LastUpdatedAtUtc = now;

            db.AssessmentCompetencyEvidence.Add(new AssessmentCompetencyEvidence
            {
                AssessmentEventId = assessmentEvent.AssessmentEventId,
                CompetencyId = competencyId,
                MembershipWeight = z,
                EffectiveEvidenceWeight = wIk,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ApplyCombinedScoreDeltaAsync(
        Guid applicationId,
        AssessmentEvent baselineEvent,
        AssessmentEvent adjustmentEvent,
        CancellationToken ct = default)
    {
        double scoreDelta = adjustmentEvent.CombinedScore - baselineEvent.CombinedScore;
        if (Math.Abs(scoreDelta) < 1e-9)
            return;

        DateTime now = DateTime.UtcNow;
        List<AssessmentCompetencyEvidence> evidence = await db.AssessmentCompetencyEvidence
            .Where(e => e.AssessmentEventId == baselineEvent.AssessmentEventId)
            .ToListAsync(ct);

        foreach (AssessmentCompetencyEvidence row in evidence)
        {
            CandidateCompetencyState? state = await db.CandidateCompetencyStates
                .FirstOrDefaultAsync(
                    s => s.CandidateApplicationId == applicationId && s.CompetencyId == row.CompetencyId,
                    ct);
            if (state is null)
                continue;

            double wIk = row.EffectiveEvidenceWeight;
            state.Alpha += wIk * scoreDelta;
            state.Beta += wIk * (-scoreDelta);
            state.Alpha = Math.Max(1e-6, state.Alpha);
            state.Beta = Math.Max(1e-6, state.Beta);
            state.MeanScore = DeterministicScoring.Mean(state.Alpha, state.Beta);
            state.EvidenceVolume = DeterministicScoring.EvidenceVolume(state.Alpha, state.Beta);
            state.LastUpdatedAtUtc = now;

            db.AssessmentCompetencyEvidence.Add(new AssessmentCompetencyEvidence
            {
                AssessmentEventId = adjustmentEvent.AssessmentEventId,
                CompetencyId = row.CompetencyId,
                MembershipWeight = row.MembershipWeight,
                EffectiveEvidenceWeight = 0,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<string> EvaluateDecisionStateAsync(Guid applicationId, CancellationToken ct = default)
    {
        CandidateApplication app = await db.CandidateApplications
            .Include(a => a.CompetencyStates)
            .FirstAsync(a => a.CandidateApplicationId == applicationId, ct);

        if (app.Status is CandidateApplicationStatuses.Approved
            or CandidateApplicationStatuses.Rejected
            or CandidateApplicationStatuses.CriticalConcern)
        {
            return app.Status;
        }

        List<CandidateCompetencyState> states = app.CompetencyStates.ToList();
        if (states.Count == 0 || states.Sum(s => s.EvidenceVolume) < 2)
        {
            app.Status = CandidateApplicationStatuses.InsufficientEvidence;
            await db.SaveChangesAsync(ct);
            return app.Status;
        }

        bool reviewReady = states.Any(s => s.EvidenceVolume >= 8 && s.MeanScore >= 0.85)
            && states.Any(s =>
                s.CompetencyId.Contains("pedagogy", StringComparison.OrdinalIgnoreCase)
                && s.EvidenceVolume >= 5
                && s.MeanScore >= 0.80);

        app.Status = reviewReady
            ? CandidateApplicationStatuses.ReviewRecommended
            : CandidateApplicationStatuses.Developing;
        await db.SaveChangesAsync(ct);
        return app.Status;
    }
}
