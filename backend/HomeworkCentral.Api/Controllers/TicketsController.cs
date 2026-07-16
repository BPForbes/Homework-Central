using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Authorize]
public class TicketsController(ITicketService tickets) : ControllerBase
{
    [HttpPost("/api/channels/by-room/{portalRoomId}/tickets")]
    public async Task<ActionResult<TicketDto>> OpenTicket(
        string portalRoomId,
        [FromBody] OpenTicketRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            string decodedPortalRoomId = Uri.UnescapeDataString(portalRoomId);
            TicketDto ticket = await tickets.OpenTicketAsync(decodedPortalRoomId, userId.Value, request, ct);
            return Ok(ticket);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("api/tickets/{ticketId:guid}")]
    public async Task<ActionResult<TicketDto>> GetTicket(Guid ticketId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        TicketDto? ticket = await tickets.GetTicketAsync(ticketId, userId.Value, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpGet("api/tickets/by-room/{roomId}")]
    public async Task<ActionResult<TicketDto>> GetTicketByRoom(string roomId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        string decodedRoomId = Uri.UnescapeDataString(roomId);
        TicketDto? ticket = await tickets.GetTicketByRoomAsync(decodedRoomId, userId.Value, ct);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("api/tickets/{ticketId:guid}/close")]
    public async Task<ActionResult<TicketDto>> CloseTicket(Guid ticketId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            TicketDto ticket = await tickets.CloseTicketAsync(ticketId, userId.Value, ct);
            return Ok(ticket);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/tickets/{ticketId:guid}/reopen")]
    public async Task<ActionResult<TicketDto>> ReopenTicket(Guid ticketId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            TicketDto ticket = await tickets.ReopenTicketAsync(ticketId, userId.Value, ct);
            return Ok(ticket);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("api/tickets/{ticketId:guid}")]
    public async Task<IActionResult> DeleteTicket(Guid ticketId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            bool deleted = await tickets.DeleteTicketAsync(ticketId, userId.Value, ct);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/tickets/{ticketId:guid}/watches")]
    public async Task<ActionResult<TicketUserWatchDto>> UpsertWatch(
        Guid ticketId,
        [FromBody] UpsertTicketWatchRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            TicketUserWatchDto? watch = await tickets.UpsertWatchAsync(ticketId, userId.Value, request, ct);
            return watch is null ? NotFound() : Ok(watch);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("api/tickets/{ticketId:guid}/analyze")]
    public async Task<ActionResult<TicketAnalyzeResultDto>> Analyze(Guid ticketId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            TicketAnalyzeResultDto result = await tickets.AnalyzeAsync(ticketId, userId.Value, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
