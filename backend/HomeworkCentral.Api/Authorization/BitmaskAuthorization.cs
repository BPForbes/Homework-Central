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
    IEffectiveMaskService effectiveMaskService,
    IAccessScopeAccessor accessScopeAccessor) : AuthorizationHandler<BitmaskRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BitmaskRequirement requirement)
    {
        AccessScope? scope = accessScopeAccessor.ResolveCurrent();
        if (scope?.AccountClass == AccountClass.DevAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        string? userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out Guid userId))
            return;

        if (await HasRequiredBitAsync(userId, requirement))
        {
            context.Succeed(requirement);
            return;
        }

        await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId);
        if (await HasRequiredBitAsync(userId, requirement))
            context.Succeed(requirement);
    }

    private async Task<bool> HasRequiredBitAsync(Guid userId, BitmaskRequirement requirement)
    {
        UserEffectiveMask? mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId);
        if (mask is null)
            return false;

        System.Collections.BitArray? bitArray = requirement.MaskType switch
        {
            MaskType.Moderation => mask.EffectiveModerationMask,
            MaskType.Feature => mask.EffectiveFeatureMask,
            MaskType.Role => mask.EffectiveRoleMask,
            MaskType.GeneralSubject => mask.GeneralSubjectMask,
            MaskType.SubjectExpertise => ResolveSubjectExpertiseMask(mask, requirement.SubjectCategory),
            MaskType.Status => mask.StatusMask,
            _ => null,
        };

        return bitArray is not null && BitMask.HasBit(bitArray, requirement.Bit);
    }

    private static System.Collections.BitArray? ResolveSubjectExpertiseMask(
        UserEffectiveMask mask,
        string? subjectCategory)
    {
        if (subjectCategory is null)
            return null;

        // Prefer FirstOrDefault only after the category is known; callers pass one category per check.
        return mask.SubjectExpertiseMasks
            .FirstOrDefault(row => row.Category == subjectCategory)
            ?.ExpertiseMask;
    }
}

public static class AuthorizationPolicyNames
{
    public const string ResourceVisibility = "ResourceVisibility";
    public const string ManageServerInfrastructure = "mask:Moderation:20";
    public const string ManagePlatformRoles = "ManagePlatformRoles";

    public static string For(MaskType maskType, short bit, string? subjectCategory = null) =>
        subjectCategory is null
            ? $"mask:{maskType}:{bit}"
            : $"mask:{maskType}:{subjectCategory}:{bit}";
}
