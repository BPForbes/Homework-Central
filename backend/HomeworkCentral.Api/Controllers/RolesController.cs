using System.Security.Claims;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleAssignmentService roleAssignments) : ControllerBase
{
    [HttpPost("grant")]
    public async Task<IActionResult> GrantRole([FromBody] GrantRoleRequest request, CancellationToken ct)
    {
        var granterId = GetUserId();
        await roleAssignments.AssignRoleAsync(granterId, request.UserId, request.RoleName, ct);
        return NoContent();
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeRole([FromBody] RevokeRoleRequest request, CancellationToken ct)
    {
        var granterId = GetUserId();
        await roleAssignments.RevokeRoleAsync(granterId, request.UserId, request.RoleName, ct);
        return NoContent();
    }

    [HttpPost("verify-user")]
    public async Task<IActionResult> VerifyUser([FromBody] VerifyUserRequest request, CancellationToken ct)
    {
        var granterId = GetUserId();
        await roleAssignments.AssignRoleAsync(granterId, request.UserId, "VerifiedUser", ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();

        return Guid.Parse(sub);
    }
}
