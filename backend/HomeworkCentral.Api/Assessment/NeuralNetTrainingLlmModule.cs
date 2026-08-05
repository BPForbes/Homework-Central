namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Single multipurpose LLM surface for neural-net synthetic training.
/// Scenario generation, embedded self-critique, and revision rewrites share one Ollama path —
/// never a second evaluator model.
/// </summary>
public interface INeuralNetTrainingLlmModule
{
    Task<SyntheticThreadScenario?> GenerateScenarioAsync(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        string? targetCategory,
        string? revisionNotes,
        CancellationToken ct);

    /// <summary>
    /// Critique from the generation payload's embedded selfCritique (or a cheap structural check).
    /// Never opens another Ollama chat request.
    /// </summary>
    SyntheticEvaluatorResult CritiqueTicket(SyntheticTicket ticket);
}

public sealed class NeuralNetTrainingLlmModule(SyntheticThreadScenarioGenerator scenarioGenerator)
    : INeuralNetTrainingLlmModule
{
    public Task<SyntheticThreadScenario?> GenerateScenarioAsync(
        NeuralTrainingMode mode,
        IReadOnlyList<string>? hints,
        string? targetCategory,
        string? revisionNotes,
        CancellationToken ct) =>
        scenarioGenerator.GenerateAsync(mode, hints, targetCategory, revisionNotes, ct);

    public SyntheticEvaluatorResult CritiqueTicket(SyntheticTicket ticket)
    {
        SyntheticThreadMessage primary = ticket.Messages.FirstOrDefault(message => !message.IsDistractor)
            ?? ticket.Messages.FirstOrDefault()
            ?? new SyntheticThreadMessage(
                0, "missing", "student", "general", string.Empty, false, 0f, new(0.5f, 1, 0.5f, []));

        if (!string.IsNullOrWhiteSpace(ticket.SelfCritiqueVerdict))
        {
            string feedback = string.IsNullOrWhiteSpace(ticket.SelfCritiqueFeedback)
                ? (string.Equals(ticket.SelfCritiqueVerdict, "REVISE", StringComparison.OrdinalIgnoreCase)
                    ? "Self-critique requested a rewrite without detailed feedback."
                    : "Self-critique accepted the scenario.")
                : ticket.SelfCritiqueFeedback!;
            return new SyntheticEvaluatorResult(
                ticket.SelfCritiqueVerdict.ToUpperInvariant(),
                ticket.ExpectedScore,
                ticket.ExpectedRelevance,
                Truncate(feedback, 2000),
                primary.TeacherApprovalEstimate ?? primary.CommunityIntent.ProposedApproval,
                primary.TeacherConfidence ?? 0.7);
        }

        if (string.IsNullOrWhiteSpace(primary.Content) || ticket.Messages.Count == 0)
        {
            return new SyntheticEvaluatorResult(
                "REVISE",
                ticket.ExpectedScore,
                ticket.ExpectedRelevance,
                "Scenario has no usable non-empty primary message.",
                0.5,
                0.4);
        }

        return new SyntheticEvaluatorResult(
            "LGTM",
            ticket.ExpectedScore,
            ticket.ExpectedRelevance,
            "Accepted without a second model; training LLM output passed structural checks.",
            primary.TeacherApprovalEstimate ?? primary.CommunityIntent.ProposedApproval,
            primary.TeacherConfidence ?? 0.65);
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "…";
}
