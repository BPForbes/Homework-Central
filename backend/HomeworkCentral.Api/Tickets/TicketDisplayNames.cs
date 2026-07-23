namespace HomeworkCentral.Api.Tickets;

public static class TicketDisplayNames
{
    public static string Open(string filterName, int displayNumber) =>
        $"Ticket - {filterName} - {displayNumber:D4}";

    public static string Closed(string filterName, int displayNumber) =>
        $"Closed - {filterName} - {displayNumber:D4}";
}
