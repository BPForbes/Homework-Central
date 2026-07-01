namespace HomeworkCentral.Api.DTOs;

public class ClaimableSubjectDto
{
    public string Name { get; set; } = null!;
    public bool Claimed { get; set; }
}

public class ClaimSubjectRequest
{
    public string SubjectName { get; set; } = null!;
}
