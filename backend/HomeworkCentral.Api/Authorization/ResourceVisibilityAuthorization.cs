using Microsoft.AspNetCore.Authorization;

namespace HomeworkCentral.Api.Authorization;

public sealed class ResourceVisibilityRequirement : IAuthorizationRequirement;

public class ResourceVisibilityHandler(IAccessScopeAccessor accessScope)
    : AuthorizationHandler<ResourceVisibilityRequirement, IScopedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceVisibilityRequirement requirement,
        IScopedResource resource)
    {
        if (accessScope.CanView(context.User, resource))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
