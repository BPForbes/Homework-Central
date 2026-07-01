using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;

namespace HomeworkCentral.Api.Utilities;

/// <summary>
/// Converts a persisted <see cref="UserEffectiveMask"/> row into the wire-format
/// <see cref="EffectiveMaskDto"/> (base64-encoded bitmasks). Shared by every place that needs to
/// hand effective masks to a client or to <see cref="Chat.IChatRoomAccessService"/> — previously
/// duplicated verbatim across <c>AuthService</c>, <c>ChatController</c>, and
/// <c>ChatMessageService</c>, which risked the mapping silently drifting between call sites.
/// </summary>
public static class EffectiveMaskMapper
{
    public static EffectiveMaskDto ToEffectiveMaskDto(this UserEffectiveMask effectiveMask)
    {
        Dictionary<string, string> subjectExpertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(
                category => category,
                category =>
                {
                    UserSubjectExpertiseMask? row = effectiveMask.SubjectExpertiseMasks
                        .FirstOrDefault(m => m.Category == category);
                    return BitMask.ToBase64(row?.ExpertiseMask ?? BitMask.Create(128));
                },
                StringComparer.Ordinal);

        return new EffectiveMaskDto
        {
            RoleMask = BitMask.ToBase64(effectiveMask.EffectiveRoleMask),
            ModerationMask = BitMask.ToBase64(effectiveMask.EffectiveModerationMask),
            FeatureMask = BitMask.ToBase64(effectiveMask.EffectiveFeatureMask),
            GeneralSubjectMask = BitMask.ToBase64(effectiveMask.GeneralSubjectMask),
            SubjectExpertiseMasks = subjectExpertiseMasks,
            StatusMask = BitMask.ToBase64(effectiveMask.StatusMask),
        };
    }
}
