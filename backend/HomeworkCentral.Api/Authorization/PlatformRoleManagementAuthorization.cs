using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Authorization;

namespace HomeworkCentral.Api.Authorization;

public sealed class PlatformRoleManagementRequirement : IAuthorizationRequirement;

/// <summary>
/// Allows platform role grant/revoke when the actor has ManageServerInfrastructure,
/// ManageRoles, or is the localhost DevAdmin account class.
/// </summary>
public class PlatformRoleManagementAuthorizationHandler(
    IEffectiveMaskService effectiveMaskService,
    IAccessScopeAccessor accessScopeAccessor) : AuthorizationHandler<PlatformRoleManagementRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformRoleManagementRequirement requirement)
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

        if (await HasPlatformRoleManagementPermissionAsync(userId))
        {
            context.Succeed(requirement);
            return;
        }

        await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId);
        if (await HasPlatformRoleManagementPermissionAsync(userId))
            context.Succeed(requirement);
    }

    private async Task<bool> HasPlatformRoleManagementPermissionAsync(Guid userId)
    {
        UserEffectiveMask? mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId);
        if (mask?.EffectiveModerationMask is null)
            return false;

        return BitMask.HasBit(mask.EffectiveModerationMask, ModerationPermissions.ManageServerInfrastructure)
            || BitMask.HasBit(mask.EffectiveModerationMask, ModerationPermissions.ManageRoles);
    }
}
