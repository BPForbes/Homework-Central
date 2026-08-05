namespace HomeworkCentral.Api.Tickets;

public static class TicketMentionKinds
{
    public const string Opened = "Ticket";
    public const string Decision = "TicketDecision";
}

public static class TicketSystemAuthor
{
    public const string DisplayName = "Homework Central Automated System";
    public static readonly Guid SenderId = Guid.Parse("00000000-0000-0000-0000-00000000c001");
}

public static class TicketTrackingModes
{
    public const string Opener = "Opener";
    public const string FromIntakeField = "FromIntakeField";
    public const string None = "None";
}

public static class TicketWatchSources
{
    public const string Intake = "Intake";
    public const string Staff = "Staff";
    public const string Model = "Model";
}

public static class TicketStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}
