using System.ComponentModel.DataAnnotations;

namespace HomeworkCentral.Api.DTOs;

public class ClaimableSubjectDto
{
    public string Name { get; set; } = null!;
    public bool Claimed { get; set; }
}

public class ClaimSubjectRequest
{
    [Required, MinLength(1), MaxLength(128)]
    public string SubjectName { get; set; } = null!;
}
