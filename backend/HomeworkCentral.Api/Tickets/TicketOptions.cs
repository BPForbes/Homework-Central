namespace HomeworkCentral.Api.Tickets;

public class TicketOptions
{
    public bool OllamaEnabled { get; set; } = true;
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "qwen3:0.6b";
    public int RequestTimeoutSeconds { get; set; } = 60;
    public double InitialConfidenceScore { get; set; } = 0.5;
    public double MaxScoreDeltaPerMessage { get; set; } = 0.15;
    public int MaxMessageCharacters { get; set; } = 2000;
    public bool StudentEnabled { get; set; } = true;
    public double StudentConfidenceThreshold { get; set; } = 0.75;
    public double ReviewerAuditRate { get; set; } = 0.10;
    public double ReviewerBlendWeight { get; set; } = 0.70;
}
