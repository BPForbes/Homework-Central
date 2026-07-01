using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Authorization;

namespace HomeworkCentral.Api.Authorization;

public enum MaskType
{
    Moderation,
    Feature,
    Role,
    GeneralSubject,
    SubjectExpertise,
    Status,
}

public sealed class BitmaskRequirement(MaskType maskType, short bit, string? subjectCategory = null)
    : IAuthorizationRequirement
{
    public MaskType MaskType { get; } = maskType;
    public short Bit { get; } = bit;
    public string? SubjectCategory { get; } = subjectCategory;
}

public class BitmaskAuthorizationHandler(
    IEffectiveMaskService effectiveMaskService) : AuthorizationHandler<BitmaskRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BitmaskRequirement requirement)
    {
        string? userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out Guid userId))
            return;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId);

        System.Collections.BitArray? bitArray = requirement.MaskType switch
        {
            MaskType.Moderation => mask.EffectiveModerationMask,
            MaskType.Feature => mask.EffectiveFeatureMask,
            MaskType.Role => mask.EffectiveRoleMask,
            MaskType.GeneralSubject => mask.GeneralSubjectMask,
            MaskType.SubjectExpertise => mask.SubjectExpertiseMasks
                .FirstOrDefault(m => m.Category == requirement.SubjectCategory)?.ExpertiseMask,
            MaskType.Status => mask.StatusMask,
            _ => null,
        };

        if (bitArray is not null && BitMask.HasBit(bitArray, requirement.Bit))
            context.Succeed(requirement);
    }
}

public static class AuthorizationPolicyNames
{
    public const string ResourceVisibility = "ResourceVisibility";

    public static string For(MaskType maskType, short bit, string? subjectCategory = null) =>
        subjectCategory is null
            ? $"mask:{maskType}:{bit}"
            : $"mask:{maskType}:{subjectCategory}:{bit}";
}
