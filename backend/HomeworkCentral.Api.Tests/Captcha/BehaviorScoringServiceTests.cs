using HomeworkCentral.Api.Captcha;
using Xunit;

namespace HomeworkCentral.Api.Tests.Captcha;

public class BehaviorScoringServiceTests
{
    private readonly BehaviorScoringService _service = new();

    [Fact]
    public void Null_telemetry_scores_zero()
    {
        Assert.Equal(0.0, _service.ComputeScore(null));
    }

    [Fact]
    public void Baseline_with_no_signals_at_all_is_below_passing_threshold()
    {
        CaptchaBehaviorDto telemetry = new()
        {
            MouseSamples = null,
            KeyIntervalsMs = null,
            TotalDurationMs = 0,
            WebdriverFlag = false,
            InteractionCount = 0,
        };

        // 0.5 baseline - 0.2 (no mouse) - 0.15 (no interaction) = 0.15, well under 0.75.
        Assert.True(_service.ComputeScore(telemetry) < 0.75);
    }

    [Fact]
    public void Webdriver_flag_is_a_strong_negative_signal()
    {
        CaptchaBehaviorDto flagged = GoodTelemetry();
        flagged.WebdriverFlag = true;

        CaptchaBehaviorDto clean = GoodTelemetry();
        clean.WebdriverFlag = false;

        Assert.True(_service.ComputeScore(flagged) < _service.ComputeScore(clean));
    }

    [Fact]
    public void Wandering_variable_speed_mouse_path_scores_higher_than_a_straight_uniform_one()
    {
        CaptchaBehaviorDto wandering = GoodTelemetry();

        CaptchaBehaviorDto straightLine = GoodTelemetry();
        List<MouseSampleDto> straightSamples = new();
        for (int i = 0; i < 10; i++)
            straightSamples.Add(new MouseSampleDto { X = i * 10, Y = i * 10, TMs = i * 50 });
        straightLine.MouseSamples = straightSamples;

        Assert.True(_service.ComputeScore(wandering) > _service.ComputeScore(straightLine));
    }

    [Fact]
    public void Faster_than_humanly_possible_duration_is_penalized()
    {
        CaptchaBehaviorDto fast = GoodTelemetry();
        fast.TotalDurationMs = 100;

        CaptchaBehaviorDto normal = GoodTelemetry();
        normal.TotalDurationMs = 4000;

        Assert.True(_service.ComputeScore(fast) < _service.ComputeScore(normal));
    }

    [Fact]
    public void Zero_interaction_is_penalized()
    {
        CaptchaBehaviorDto noInteraction = GoodTelemetry();
        noInteraction.InteractionCount = 0;

        CaptchaBehaviorDto withInteraction = GoodTelemetry();
        withInteraction.InteractionCount = 3;

        Assert.True(_service.ComputeScore(noInteraction) < _service.ComputeScore(withInteraction));
    }

    [Fact]
    public void Uniform_scripted_keystroke_timing_is_penalized_relative_to_natural_rhythm()
    {
        CaptchaBehaviorDto scripted = GoodTelemetry();
        scripted.KeyIntervalsMs = [100, 100, 100, 100, 100, 100];

        CaptchaBehaviorDto natural = GoodTelemetry();
        natural.KeyIntervalsMs = [120, 95, 180, 60, 140, 110];

        Assert.True(_service.ComputeScore(scripted) < _service.ComputeScore(natural));
    }

    [Fact]
    public void Score_is_always_clamped_between_zero_and_one()
    {
        CaptchaBehaviorDto worstCase = new()
        {
            MouseSamples = null,
            KeyIntervalsMs = [100, 100, 100, 100],
            TotalDurationMs = 1,
            WebdriverFlag = true,
            InteractionCount = 0,
        };

        double score = _service.ComputeScore(worstCase);
        Assert.InRange(score, 0.0, 1.0);
    }

    private static CaptchaBehaviorDto GoodTelemetry()
    {
        int[] dxs = [3, 15, -2, 20, -1, 18, 2, 16, -3, 14];
        int[] dys = [2, -10, 3, -12, 2, 9, -1, -11, 4, 8];
        int[] dts = [80, 20, 90, 15, 85, 18, 95, 16, 88, 22];

        List<MouseSampleDto> mouseSamples = new();
        int x = 10, y = 10, t = 0;
        for (int i = 0; i < dxs.Length; i++)
        {
            x += dxs[i];
            y += dys[i];
            t += dts[i];
            mouseSamples.Add(new MouseSampleDto { X = x, Y = y, TMs = t });
        }

        return new CaptchaBehaviorDto
        {
            MouseSamples = mouseSamples,
            TotalDurationMs = 4000,
            WebdriverFlag = false,
            InteractionCount = 3,
        };
    }
}
