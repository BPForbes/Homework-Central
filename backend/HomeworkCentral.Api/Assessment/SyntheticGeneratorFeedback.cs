namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Balanced hints from LLM-2 audits / teacher labels for the next LLM-1 scenario.
/// Caps revise pressure so generator feedback does not collapse diversity.
/// </summary>
public sealed class SyntheticGeneratorFeedbackBuffer
{
    private const int MaxHints = 6;
    private const int MaxReviseHints = 3;
    private readonly List<string> _hints = [];
    private int _reviseCount;
    private int _acceptCount;

    public IReadOnlyList<string> Hints => _hints;

    public void RecordAudit(string verdict, string feedback, string category)
    {
        string trimmedFeedback = Truncate(feedback, 160);
        if (string.IsNullOrWhiteSpace(trimmedFeedback))
            return;

        bool revise = verdict.Contains("REVISE", StringComparison.OrdinalIgnoreCase)
            || verdict.Contains("reject", StringComparison.OrdinalIgnoreCase);
        if (!revise)
        {
            _acceptCount++;
            AddHint($"Keep diversity like '{Truncate(category, 40)}' (audit accepted): {trimmedFeedback}");
            return;
        }

        _reviseCount++;
        if (_reviseCount > MaxReviseHints && _reviseCount > _acceptCount)
            return;

        AddHint($"Prefer harder negatives around '{Truncate(category, 40)}': {trimmedFeedback}");
    }

    public void RecordTeacherGap(string category, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return;
        AddHint($"Cover '{Truncate(category, 40)}' with: {Truncate(note, 140)}");
    }

    private void AddHint(string hint)
    {
        if (_hints.Any(existing => string.Equals(existing, hint, StringComparison.OrdinalIgnoreCase)))
            return;
        _hints.Add(hint);
        while (_hints.Count > MaxHints)
            _hints.RemoveAt(0);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
