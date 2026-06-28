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
        IActionResult? validation = ValidateRoleRequest(request.UserId, request.RoleName);
        if (validation is not null)
            return validation;

        try
        {
            Guid granterId = GetUserId();
            await roleAssignments.AssignRoleAsync(granterId, request.UserId, request.RoleName, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return MapInvalidOperation(ex);
        }
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeRole([FromBody] RevokeRoleRequest request, CancellationToken ct)
    {
        IActionResult? validation = ValidateRoleRequest(request.UserId, request.RoleName);
        if (validation is not null)
            return validation;

        try
        {
            Guid granterId = GetUserId();
            await roleAssignments.RevokeRoleAsync(granterId, request.UserId, request.RoleName, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return MapInvalidOperation(ex);
        }
    }

    [HttpPost("verify-user")]
    public async Task<IActionResult> VerifyUser([FromBody] VerifyUserRequest request, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty)
            return BadRequest(new { error = "UserId is required." });

        try
        {
            Guid granterId = GetUserId();
            await roleAssignments.AssignRoleAsync(granterId, request.UserId, "VerifiedUser", ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return MapInvalidOperation(ex);
        }
    }

    private static IActionResult? ValidateRoleRequest(Guid userId, string? roleName)
    {
        if (userId == Guid.Empty)
            return new BadRequestObjectResult(new { error = "UserId is required." });

        if (string.IsNullOrWhiteSpace(roleName))
            return new BadRequestObjectResult(new { error = "RoleName is required." });

        return null;
    }

    private static IActionResult MapInvalidOperation(InvalidOperationException ex)
    {
        if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return new NotFoundObjectResult(new { error = ex.Message });

        return new BadRequestObjectResult(new { error = ex.Message });
    }

    private Guid GetUserId()
    {
        string? sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();

        return Guid.Parse(sub);
    }
}
