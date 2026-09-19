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
            string normalizedVerdict = NormalizeVerdict(ticket.SelfCritiqueVerdict) ?? ticket.SelfCritiqueVerdict.Trim().ToUpperInvariant();
            string feedback = string.IsNullOrWhiteSpace(ticket.SelfCritiqueFeedback)
                ? (string.Equals(normalizedVerdict, "REVISE", StringComparison.Ordinal)
                    ? "Training LLM self-critique requested a rewrite."
                    : "Training LLM self-critique accepted the scenario (generate+evaluate).")
                : ticket.SelfCritiqueFeedback!;
            return new SyntheticEvaluatorResult(
                normalizedVerdict,
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
                "Training LLM scenario has no usable non-empty primary message.",
                0.5,
                0.4);
        }

        // Generation JSON omitted selfCritique; local structural gate still runs on the same
        // generate+evaluate path (no separate reviewer model).
        return new SyntheticEvaluatorResult(
            "LGTM",
            ticket.ExpectedScore,
            ticket.ExpectedRelevance,
            "Training LLM · structural accept (generate+evaluate; embedded selfCritique omitted).",
            primary.TeacherApprovalEstimate ?? primary.CommunityIntent.ProposedApproval,
            primary.TeacherConfidence ?? 0.65);
    }

    /// <summary>Maps common LLM phrasings onto the LGTM / REVISE contract.</summary>
    internal static string? NormalizeVerdict(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string token = raw.Trim().TrimEnd('.', '!', ':').ToUpperInvariant();
        return token switch
        {
            "LGTM" or "OK" or "OKAY" or "PASS" or "PASSED" or "ACCEPT" or "ACCEPTED"
                or "APPROVE" or "APPROVED" or "GOOD" or "LOOKS GOOD" => "LGTM",
            "REVISE" or "REJECT" or "REJECTED" or "FAIL" or "FAILED" or "REDO"
                or "REWRITE" or "NEEDS WORK" => "REVISE",
            _ => null,
        };
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "…";
}
