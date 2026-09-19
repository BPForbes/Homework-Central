namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Process-wide parse memo for vector <c>EmbeddingJson</c>. Values live in
/// the Rust LRU when <c>hc_lru_*</c> is bound.
/// </summary>
internal static class VectorRetrievalMemo
{
    internal const int ParseCapacity = 512;
    private static readonly HostLru Parsed = new(ParseCapacity);

    internal static bool TryParse(string json, out float[]? values)
    {
        if (Parsed.TryGetFloats(json, out float[] cached))
        {
            values = cached;
            return true;
        }

        values = null;
        return false;
    }

    internal static void PutParse(string json, float[] values) =>
        Parsed.PutFloats(json, values);

    internal static void Reset() => Parsed.Clear();
}
