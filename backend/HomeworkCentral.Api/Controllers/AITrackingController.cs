using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/ai-tracking")]
[Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
public sealed class AITrackingController(IAITrackingService tracking) : ControllerBase
{
    [HttpGet("lineages")]
    public async Task<ActionResult<IReadOnlyList<AIModelLineageDto>>> ListLineages(CancellationToken ct) =>
        Ok(await tracking.ListLineagesAsync(ct));

    [HttpGet("lineages/{lineageSlug}/categories")]
    public async Task<ActionResult<IReadOnlyList<AICategoryDto>>> ListCategories(string lineageSlug, CancellationToken ct) =>
        Ok(await tracking.ListCategoriesAsync(lineageSlug, ct));

    [HttpPost("lineages")]
    public async Task<ActionResult<AIModelLineageDto>> RegisterLineage(
        [FromBody] RegisterAIModelLineageRequest request,
        CancellationToken ct)
    {
        try
        {
            AIModelLineageDto created = await tracking.RegisterCustomLineageAsync(request, ct);
            return CreatedAtAction(nameof(ListCategories), new { lineageSlug = created.Slug }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("lineages/{lineageSlug}")]
    public async Task<IActionResult> DeleteLineage(string lineageSlug, CancellationToken ct)
    {
        bool deleted = await tracking.DeleteCustomLineageAsync(lineageSlug, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<PagedResultDto<AITrackingSessionDto>>> QuerySessions(
        [FromQuery] string? lineageSlug,
        [FromQuery] Guid? ticketId,
        [FromQuery] Guid? createdByUserId,
        [FromQuery] DateTime? beforeUtc,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await tracking.QuerySessionsAsync(lineageSlug, ticketId, createdByUserId, beforeUtc, limit, ct));

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<ActionResult<AITrackingSessionDto>> GetSession(long sessionId, CancellationToken ct)
    {
        AITrackingSessionDto? session = await tracking.GetSessionAsync(sessionId, ct);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpDelete("sessions/{sessionId:long}")]
    public async Task<IActionResult> DeleteSession(long sessionId, CancellationToken ct)
    {
        bool deleted = await tracking.DeleteSessionAsync(sessionId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpDelete("tickets/{ticketId:guid}/sessions")]
    public async Task<ActionResult<AITrackingDeleteResultDto>> DeleteTicketSessions(Guid ticketId, CancellationToken ct) =>
        Ok(new AITrackingDeleteResultDto
        {
            DeletedSessionCount = await tracking.DeleteSessionsForTicketAsync(ticketId, ct),
        });

    [HttpDelete("lineages/{lineageSlug}/sessions")]
    public async Task<ActionResult<AITrackingDeleteResultDto>> DeleteLineageSessions(string lineageSlug, CancellationToken ct) =>
        Ok(new AITrackingDeleteResultDto
        {
            DeletedSessionCount = await tracking.DeleteSessionsForLineageAsync(lineageSlug, ct),
        });
}
