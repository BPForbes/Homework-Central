namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Topology unit for chat-monitoring neural nets. Runtime compute stays matrix-based;
/// nodes carry identity/labels for replay, visualizer, and checkpoint parameter maps.
/// </summary>
public sealed class Node
{
    public Node(
        int index,
        string nodeId,
        string layerId,
        int layerIndex,
        string label,
        int? featureIndex,
        bool trainable)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("Node id is required.", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("Layer id is required.", nameof(layerId));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label is required.", nameof(label));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (layerIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        Index = index;
        NodeId = nodeId;
        LayerId = layerId;
        LayerIndex = layerIndex;
        Label = label;
        FeatureIndex = featureIndex;
        Trainable = trainable;
    }

    public int Index { get; }
    public string NodeId { get; }
    public string LayerId { get; }
    public int LayerIndex { get; }
    public string Label { get; }
    public int? FeatureIndex { get; }
    public bool Trainable { get; }

    /// <summary>Last forward pre-activation (logit / linear sum) when the network updates node state.</summary>
    public float PreActivation { get; set; }

    /// <summary>Last forward activation after the layer nonlinearity / head.</summary>
    public float Activation { get; set; }

    public ReplayNode ToReplayNode() =>
        new(Index, NodeId, LayerId, Label, FeatureIndex, Trainable);
}
