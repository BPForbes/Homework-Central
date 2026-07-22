namespace HomeworkCentral.Api.Assessment;

/// <summary>Guards IEEE-754 values that System.Text.Json cannot emit by default (NaN / ±Infinity).</summary>
internal static class NeuralNetFinite
{
    public static float OrZero(float value) => float.IsFinite(value) ? value : 0f;

    public static double OrZero(double value) => double.IsFinite(value) ? value : 0d;

    public static float ClampFinite(float value, float min, float max) =>
        Math.Clamp(OrZero(value), min, max);
}
