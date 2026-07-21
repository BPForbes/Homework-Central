namespace HomeworkCentral.Api.Assessment;

/// <summary>Tunable synthetic-training efficiency knobs (LLM labels, local stop, batching, traces).</summary>
public sealed class NeuralNetTrainingOptions
{
    /// <summary>Fraction of cross-domain tickets kept as negative controls in Both mode (0..1).</summary>
    public double CrossDomainSampleRate { get; set; } = 0.15;

    /// <summary>Fraction of trained messages that also receive an independent LLM-2 audit (0..1).</summary>
    public double AuditSampleRate { get; set; } = 0.05;

    /// <summary>Local SGD epochs per message when training against a fixed teacher label.</summary>
    public int LocalEpochs { get; set; } = 24;

    /// <summary>Stop local training when |evidence − target| is below this.</summary>
    public float EvidenceTolerance { get; set; } = 0.12f;

    /// <summary>Stop local training when |relevance − target| is below this.</summary>
    public float RelevanceTolerance { get; set; } = 0.12f;

    /// <summary>Stop local training when total BCE loss is at or below this.</summary>
    public float LossStopThreshold { get; set; } = 0.35f;

    /// <summary>Flush accumulated DB rows / vector upserts after this many training examples.</summary>
    public int PersistenceBatchSize { get; set; } = 50;

    /// <summary>Use compact per-epoch replay (loss + grad norm) instead of full parameter deltas.</summary>
    public bool CompactReplay { get; set; } = true;

    /// <summary>Fraction of messages that still capture full parameter-level traces when CompactReplay is on.</summary>
    public double FullTraceSampleRate { get; set; } = 0.02;
}
