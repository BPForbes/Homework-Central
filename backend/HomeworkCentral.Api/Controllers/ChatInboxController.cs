using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatInboxController(IChatInboxService inboxService) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<ActionResult<IReadOnlyList<ChatInboxItemDto>>> GetInbox(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await inboxService.GetInboxAsync(userId.Value, ct));
    }

    [HttpGet("inbox/summary")]
    public async Task<ActionResult<ChatInboxSummaryDto>> GetInboxSummary(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await inboxService.GetSummaryAsync(userId.Value, ct));
    }

    [HttpPost("inbox/{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool updated = await inboxService.MarkReadAsync(userId.Value, notificationId, ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("inbox/read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        await inboxService.MarkAllReadAsync(userId.Value, ct);
        return NoContent();
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
