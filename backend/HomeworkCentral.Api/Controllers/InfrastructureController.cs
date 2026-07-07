using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/infrastructure")]
[Authorize]
public class InfrastructureController(IInfrastructureService infrastructure) : ControllerBase
{
    [HttpGet("roles")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<IReadOnlyList<CustomRoleDto>>> ListRoles(CancellationToken ct) =>
        Ok(await infrastructure.ListCustomRolesAsync(ct));

    [HttpPost("roles")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<CustomRoleDto>> CreateRole([FromBody] CreateCustomRoleRequest request, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            return Ok(await infrastructure.CreateCustomRoleAsync(userId.Value, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("roles/{roleId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<CustomRoleDto>> UpdateRole(
        Guid roleId,
        [FromBody] UpdateCustomRoleRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            CustomRoleDto? role = await infrastructure.UpdateCustomRoleAsync(userId.Value, roleId, request, ct);
            return role is null ? NotFound() : Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("roles/{roleId:guid}/placement")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<IActionResult> SetRolePlacement(
        Guid roleId,
        [FromBody] SetRoleClaimPlacementRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            bool updated = await infrastructure.SetRoleClaimPlacementAsync(userId.Value, roleId, request, ct);
            return updated ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("roles/{roleId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<IActionResult> DeleteRole(Guid roleId, CancellationToken ct)
    {
        bool deleted = await infrastructure.DeleteCustomRoleAsync(roleId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("channels")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<IReadOnlyList<CustomChannelDto>>> ListChannels(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await infrastructure.ListCustomChannelsAsync(userId.Value, ct));
    }

    [HttpPost("channels")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<CustomChannelDto>> CreateChannel(
        [FromBody] CreateCustomChannelRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            return Ok(await infrastructure.CreateCustomChannelAsync(userId.Value, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("channels/{channelId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<CustomChannelDto>> UpdateChannel(
        Guid channelId,
        [FromBody] UpdateCustomChannelRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            CustomChannelDto? channel = await infrastructure.UpdateCustomChannelAsync(userId.Value, channelId, request, ct);
            return channel is null ? NotFound() : Ok(channel);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("channels/{channelId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<IActionResult> ArchiveChannel(Guid channelId, CancellationToken ct)
    {
        bool archived = await infrastructure.ArchiveCustomChannelAsync(channelId, ct);
        return archived ? NoContent() : NotFound();
    }

    [HttpGet("channels/by-room/{roomId}/claimable-roles")]
    public async Task<ActionResult<IReadOnlyList<ClaimableCustomRoleDto>>> GetClaimableRoles(string roomId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await infrastructure.GetClaimableRolesAsync(userId.Value, Uri.UnescapeDataString(roomId), ct));
    }

    [HttpPost("roles/{roleId:guid}/claim")]
    public async Task<IActionResult> ClaimRole(Guid roleId, [FromQuery] string roomId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool claimed = await infrastructure.ClaimCustomRoleAsync(
            userId.Value,
            roleId,
            Uri.UnescapeDataString(roomId),
            ct);
        return claimed ? NoContent() : BadRequest(new { message = "Could not claim that role." });
    }

    [HttpDelete("roles/{roleId:guid}/claim")]
    public async Task<IActionResult> UnclaimRole(Guid roleId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool removed = await infrastructure.UnclaimCustomRoleAsync(userId.Value, roleId, ct);
        return removed ? NoContent() : NotFound();
    }

    [HttpGet("channels/by-room/{roomId}")]
    public async Task<ActionResult<CustomChannelDto>> GetChannel(string roomId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        CustomChannelDto? channel = await infrastructure.GetChannelForUserAsync(
            userId.Value,
            Uri.UnescapeDataString(roomId),
            ct);
        return channel is null ? NotFound() : Ok(channel);
    }

    [HttpGet("roles/{roleId:guid}/access-risk")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<ModerationRiskWarningDto>> PreviewAccessRisk(
        Guid roleId,
        [FromQuery] bool isPublicRoom,
        CancellationToken ct)
    {
        ModerationRiskWarningDto? warning = await infrastructure.PreviewAccessRuleRiskAsync(roleId, isPublicRoom, ct);
        return Ok(warning ?? new ModerationRiskWarningDto { RequiresPassword = false });
    }

    [HttpGet("users/search")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<IReadOnlyList<InfrastructureUserLookupDto>>> SearchUsers(
        [FromQuery] string q,
        CancellationToken ct) =>
        Ok(await infrastructure.SearchUsersAsync(q, ct));

    [HttpGet("users/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<ActionResult<InfrastructureUserLookupDto>> GetUser(Guid userId, CancellationToken ct)
    {
        InfrastructureUserLookupDto? user = await infrastructure.GetUserWithCustomRolesAsync(userId, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("users/{userId:guid}/roles/{roleId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<IActionResult> AssignRoleToUser(Guid userId, Guid roleId, CancellationToken ct)
    {
        Guid? actorId = GetUserId();
        if (actorId is null)
            return Unauthorized();

        bool assigned = await infrastructure.AdminAssignCustomRoleAsync(actorId.Value, userId, roleId, ct);
        return assigned ? NoContent() : NotFound();
    }

    [HttpDelete("users/{userId:guid}/roles/{roleId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
    public async Task<IActionResult> RevokeRoleFromUser(Guid userId, Guid roleId, CancellationToken ct)
    {
        bool removed = await infrastructure.AdminRevokeCustomRoleAsync(userId, roleId, ct);
        return removed ? NoContent() : NotFound();
    }

    private Guid? GetUserId()
    {
        string? userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return userIdClaim is not null && Guid.TryParse(userIdClaim, out Guid userId)
            ? userId
            : null;
    }
}
