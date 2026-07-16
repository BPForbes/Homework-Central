namespace HomeworkCentral.Api.Models;

public class Ticket
{
    public Guid TicketId { get; set; }
    public Guid PortalChannelId { get; set; }
    public TicketPortalConfig Portal { get; set; } = null!;
    public Guid ChatChannelId { get; set; }
    public CustomChannel ChatChannel { get; set; } = null!;
    public string RoomId { get; set; } = null!;
    public int DisplayNumber { get; set; }
    public string Purpose { get; set; } = null!;
    public Guid OpenedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string IntakeAnswersJson { get; set; } = "[]";

    public ICollection<TicketUserWatch> Watches { get; set; } = [];
}

public class TicketUserWatch
{
    public Guid WatchId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid TrackedUserId { get; set; }
    public string ContextLabel { get; set; } = null!;
    public bool IsActive { get; set; }
    public Guid SetByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Source { get; set; } = "Staff";
}
