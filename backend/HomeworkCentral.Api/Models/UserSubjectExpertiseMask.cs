using System.Collections;

namespace HomeworkCentral.Api.Models;

public class UserSubjectExpertiseMask
{
    public Guid UserId { get; set; }
    public string Category { get; set; } = null!;
    public BitArray ExpertiseMask { get; set; } = new(128);

    public UserEffectiveMask EffectiveMask { get; set; } = null!;
}
