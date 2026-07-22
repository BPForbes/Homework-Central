using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Produces the V2 report envelope and validates cascade evidence-scorer topologies.
/// Numeric checkpoints are packed as little-endian IEEE-754 float32 before JSON encoding.
/// Limits cover dense MLP graphs (e.g. moderation 86→48→72→64→56→103 ≈ 21k edges).
/// </summary>
public static class NeuralNetReplaySerializer
{
    /// <summary>Moderation evidence scorer is 429 nodes today; leave headroom for taxonomy growth.</summary>
    public const int MaxNodes = 2048;
    /// <summary>Dense layer edges for the largest cascade scorer (~21k) plus headroom.</summary>
    public const int MaxEdges = 65536;
    public const int MaxFrames = 100_000;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Training traces can still surface extreme floats; never fail the whole session on JSON.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(NeuralNetReplayReportV2 report) => JsonSerializer.Serialize(report, Options);

    public static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static ReplayIntegrity CreateIntegrity(
        NeuralNetTopologySnapshot topology,
        NeuralNetParameterSnapshot initial,
        NeuralNetParameterSnapshot final,
        string reportContentWithoutIntegrity)
        => new("hc-replay-canonical-json-v1", "sha-256",
            ComputeSha256(JsonSerializer.Serialize(topology, Options)), initial.Checksum, final.Checksum,
            ComputeSha256(reportContentWithoutIntegrity));

    public static void Validate(NeuralNetReplayReportV2 report)
    {
        if (report.Topology.Parameters.Count != report.InitialParameters.ParameterCount ||
            report.Topology.Parameters.Count != report.FinalParameters.ParameterCount)
            throw new InvalidDataException("Replay parameter snapshot length does not match topology.");
        for (int index = 0; index < report.Topology.Parameters.Count; index++)
            if (report.Topology.Parameters[index].Index != index)
                throw new InvalidDataException("Replay parameter indices must be contiguous and ordered.");
        if (report.Topology.Nodes.Count > MaxNodes || report.Topology.Edges.Count > MaxEdges || report.Frames.Count > MaxFrames)
            throw new InvalidDataException("Replay exceeds supported V2 import limits.");
    }
}
