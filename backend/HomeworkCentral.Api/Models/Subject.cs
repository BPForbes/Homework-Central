namespace HomeworkCentral.Api.Models;

public class Subject
{
    public Guid SubjectId { get; set; }
    public Guid? ParentSubjectId { get; set; }
    public string SubjectMask { get; set; } = null!;
    public short BitIndex { get; set; }
    public string Name { get; set; } = null!;

    public Subject? ParentSubject { get; set; }
    public ICollection<Subject> ChildSubjects { get; set; } = new List<Subject>();
    public ICollection<UserSubject> UserSubjects { get; set; } = new List<UserSubject>();
}
