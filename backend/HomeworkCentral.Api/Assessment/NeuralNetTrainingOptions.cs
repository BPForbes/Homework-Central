namespace HomeworkCentral.Api.Assessment;

/// <summary>Tunable synthetic-training efficiency knobs (LLM labels, local stop, batching, traces).</summary>
public sealed class NeuralNetTrainingOptions
{
    /// <summary>Fraction of cross-domain tickets kept as negative controls in Both mode (0..1).</summary>
    public double CrossDomainSampleRate { get; set; } = 0.15;

    /// <summary>
    /// Reserved. Training-time second-pass Ollama audits are disabled; keep at 0.
    /// </summary>
    public double AuditSampleRate { get; set; } = 0;

    /// <summary>
    /// Fraction of LLM-1 tickets that apply embedded selfCritique / structural critique.
    /// Critique itself does not open a second Ollama call; set 1.0 to revise from every ticket.
    /// </summary>
    public double GeneratorAuditSampleRate { get; set; } = 1;

    /// <summary>Local SGD epochs per message when training against a fixed teacher label.</summary>
    public int LocalEpochs { get; set; } = 12;

    /// <summary>Stop local training when |evidence − target| is below this.</summary>
    public float EvidenceTolerance { get; set; } = 0.12f;

    /// <summary>Stop local training when |relevance − target| is below this.</summary>
    public float RelevanceTolerance { get; set; } = 0.12f;

    /// <summary>Stop local training when total BCE loss is at or below this.</summary>
    public float LossStopThreshold { get; set; } = 0.35f;

    /// <summary>Flush accumulated DB rows / vector upserts after this many training examples.</summary>
    public int PersistenceBatchSize { get; set; } = 50;

    /// <summary>
    /// Mini-batch size for SGD (3Blue1Brown average cost). 1 = online SGD per example.
    /// </summary>
    public int MiniBatchSize { get; set; } = 8;

    /// <summary>Use compact per-epoch replay (loss + grad norm) instead of full parameter deltas.</summary>
    public bool CompactReplay { get; set; } = true;

    /// <summary>Fraction of messages that still capture full parameter-level traces when CompactReplay is on.</summary>
    public double FullTraceSampleRate { get; set; } = 0.12;

    /// <summary>
    /// When true, missing teacher labels use the deterministic fallback instead of a second
    /// per-message LLM call (much faster; slightly noisier labels).
    /// </summary>
    public bool PreferDeterministicTeacherLabels { get; set; } = true;

    /// <summary>Reserved retry budget for retired training-time Ollama audits.</summary>
    public int AuditMaxAttempts { get; set; } = 1;

    /// <summary>
    /// How many times LLM-1 may rewrite a scenario that its own selfCritique asked to revise
    /// before training continues with the best available attempt. 0 disables reworking
    /// (feedback becomes a hint only).
    /// </summary>
    public int GeneratorRevisionMaxAttempts { get; set; } = 1;

    /// <summary>
    /// When true, silent/compact mini-batch SGD uses LibTorch (CUDA if available, else CPU).
    /// Full replay traces stay on Math.NET for layer-level fidelity. Falls back to Math.NET
    /// if LibTorch fails to load.
    /// </summary>
    public bool PreferTorchAccelerator { get; set; } = true;

    /// <summary>
    /// LibTorch device preference: <c>auto</c> (CUDA when present), <c>cpu</c>, or <c>cuda</c>.
    /// GPU hosts must reference <c>TorchSharp-cuda-linux</c> / <c>TorchSharp-cuda-windows</c>
    /// instead of the default <c>TorchSharp-cpu</c> package.
    /// </summary>
    public string TorchDevice { get; set; } = "auto";
}
