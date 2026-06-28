using System.Collections;

namespace HomeworkCentral.Api.Models;

public class UserEffectiveMask
{
    public Guid UserId { get; set; }
    public BitArray EffectiveRoleMask { get; set; } = new(64);
    public BitArray EffectiveModerationMask { get; set; } = new(256);
    public BitArray EffectiveFeatureMask { get; set; } = new(256);
    public BitArray GeneralSubjectMask { get; set; } = new(128);
    public BitArray StatusMask { get; set; } = new(64);
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<UserSubjectExpertiseMask> SubjectExpertiseMasks { get; set; } = new List<UserSubjectExpertiseMask>();
}
