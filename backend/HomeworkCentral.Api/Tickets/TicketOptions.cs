namespace HomeworkCentral.Api.Tickets;

public class TicketOptions
{
    public bool OllamaEnabled { get; set; } = true;
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "qwen3:1.7b";
    public int RequestTimeoutSeconds { get; set; } = 60;
}
