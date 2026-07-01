namespace HomeworkCentral.Api.Captcha;

/// <summary>
/// Starts every attempt at a neutral 0.5 and applies weighted, explainable deltas for each signal,
/// clamped to [0, 1]. Weights are additive rather than multiplicative so one missing/neutral signal
/// (e.g. a puzzle type with no typing involved) never zeroes out the whole score.
/// </summary>
public sealed class BehaviorScoringService : IBehaviorScoringService
{
    private const double Baseline = 0.5;

    // Bounds on how much telemetry is actually processed, independent of how much a caller sends —
    // protects against a caller submitting an absurdly large array purely to burn CPU.
    private const int MaxMouseSamples = 400;
    private const int MaxKeyIntervals = 200;

    public double ComputeScore(CaptchaBehaviorDto? telemetry)
    {
        if (telemetry is null)
            return 0.0;

        double score = Baseline;

        if (telemetry.WebdriverFlag)
            score -= 0.4;

        score += ScoreMouseMovement(telemetry.MouseSamples);
        score += ScoreKeystrokeTiming(telemetry.KeyIntervalsMs);
        score += ScoreDuration(telemetry.TotalDurationMs);
        score += ScoreInteraction(telemetry.InteractionCount, telemetry.TotalDurationMs);

        return Math.Clamp(score, 0.0, 1.0);
    }

    private static double ScoreMouseMovement(List<MouseSampleDto>? samples)
    {
        if (samples is null || samples.Count == 0)
            return -0.2;

        List<MouseSampleDto> capped = samples.Count > MaxMouseSamples
            ? samples.Take(MaxMouseSamples).ToList()
            : samples;

        double pathLength = 0;
        double sumSpeed = 0;
        double sumSpeedSq = 0;
        int speedSamples = 0;

        for (int i = 1; i < capped.Count; i++)
        {
            double dx = capped[i].X - capped[i - 1].X;
            double dy = capped[i].Y - capped[i - 1].Y;
            double dist = Math.Sqrt((dx * dx) + (dy * dy));
            pathLength += dist;

            int dt = Math.Max(1, capped[i].TMs - capped[i - 1].TMs);
            double speed = dist / dt;
            sumSpeed += speed;
            sumSpeedSq += speed * speed;
            speedSamples++;
        }

        if (speedSamples == 0 || pathLength < 5)
            return -0.1;

        double straightLineDist = Distance(capped[0], capped[^1]);
        double directness = pathLength > 0 ? straightLineDist / pathLength : 1.0;

        double meanSpeed = sumSpeed / speedSamples;
        double variance = (sumSpeedSq / speedSamples) - (meanSpeed * meanSpeed);
        double speedStdDev = Math.Sqrt(Math.Max(0, variance));

        double bonus = 0;

        // A perfectly straight line across many samples is a hallmark of a linearly-interpolated
        // synthetic move; real hand motion wanders.
        if (directness < 0.9)
            bonus += 0.1;

        // Real hand motion accelerates and decelerates; a scripted move often has near-constant speed.
        if (speedStdDev > 0.05)
            bonus += 0.1;

        return bonus;
    }

    private static double Distance(MouseSampleDto a, MouseSampleDto b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double ScoreKeystrokeTiming(List<int>? intervalsMs)
    {
        // Not every challenge type involves typing (maze/tile-rotate don't), so absent timing data
        // is neutral rather than penalized — only present-but-suspicious timing is scored down.
        if (intervalsMs is null || intervalsMs.Count == 0)
            return 0.0;

        List<int> capped = intervalsMs.Count > MaxKeyIntervals
            ? intervalsMs.Take(MaxKeyIntervals).ToList()
            : intervalsMs;

        double mean = capped.Average();
        double variance = capped.Select(v => (v - mean) * (v - mean)).Average();
        double stdDev = Math.Sqrt(variance);

        if (stdDev < 10)
            return -0.15; // suspiciously uniform — scripted/programmatic input

        if (stdDev is >= 15 and <= 400)
            return 0.15; // natural human typing rhythm

        return 0.0;
    }

    private static double ScoreDuration(int totalDurationMs)
    {
        if (totalDurationMs <= 0)
            return 0.0;

        if (totalDurationMs < 600)
            return -0.25; // faster than a human can perceive and solve the challenge

        if (totalDurationMs is >= 600 and <= 60_000)
            return 0.1;

        return 0.0;
    }

    private static double ScoreInteraction(int interactionCount, int totalDurationMs)
    {
        if (interactionCount <= 0)
            return -0.15; // answer arrived with zero recorded interaction — likely injected directly

        if (totalDurationMs > 0)
        {
            double perSecond = interactionCount / (totalDurationMs / 1000.0);
            if (perSecond > 20)
                return -0.15; // implausibly fast, sustained interaction rate
        }

        return 0.05;
    }
}
