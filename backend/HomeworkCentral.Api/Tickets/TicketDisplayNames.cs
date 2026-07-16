namespace HomeworkCentral.Api.Tickets;

public static class TicketDisplayNames
{
    public static string Open(string purpose, int displayNumber) =>
        $"Ticket - {purpose} - {displayNumber:D4}";

    public static string Closed(string purpose, int displayNumber) =>
        $"Closed - {purpose} - {displayNumber:D4}";
}
