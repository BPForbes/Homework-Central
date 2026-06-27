namespace HomeworkCentral.Api.Models;

public class UserSubject
{
    public Guid UserId { get; set; }
    public Guid SubjectId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }

    public User User { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
    public User? AssignedByUser { get; set; }
}
