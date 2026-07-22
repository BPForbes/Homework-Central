using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

/// <summary>
/// Infrastructure-admin Neural Net endpoints for reviewer feedback, synthetic training sessions,
/// visualizer topology, and V2 replay downloads. Requires
/// <see cref="AuthorizationPolicyNames.ManageServerInfrastructure"/>. See docs/tickets.md.
/// </summary>
[ApiController]
[Route("api/neural-net")]
[Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
public sealed class NeuralNetController(INeuralNetTrainingService training) : ControllerBase
{
    /// <summary>Pending reviewer score events awaiting staff approve/reject.</summary>
    [HttpGet("training-feedback")]
    public async Task<ActionResult<IReadOnlyList<NeuralNetTrainingFeedbackDto>>> GetTrainingFeedback(CancellationToken ct) =>
        Ok(await training.GetPendingFeedbackAsync(ct));

    /// <summary>Approves labels into a training example and updates the in-process student model.</summary>
    [HttpPost("training-feedback/{scoreEventId:guid}/approve")]
    public async Task<ActionResult<NeuralNetTrainingFeedbackDto>> Approve(Guid scoreEventId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        try { return Ok(await training.ApproveAsync(scoreEventId, userId.Value, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Rejects feedback so it cannot train the student.</summary>
    [HttpPost("training-feedback/{scoreEventId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid scoreEventId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        try { await training.RejectAsync(scoreEventId, userId.Value, ct); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Aggregate training-data counts for the admin data-management panel.</summary>
    [HttpGet("data-management")]
    public async Task<ActionResult<NeuralNetDataManagementDto>> GetDataManagement(CancellationToken ct) =>
        Ok(await training.GetDataManagementAsync(ct));

    /// <summary>Cascade topology summaries for the admin visualizer.</summary>
    [HttpGet("visualizer")]
    public async Task<ActionResult<NeuralNetVisualizerDto>> GetVisualizer(CancellationToken ct) =>
        Ok(await training.GetVisualizerAsync(ct));

    /// <summary>Queues a synthetic training session; returns 202 with the session DTO.</summary>
    [HttpPost("training")]
    public async Task<ActionResult<NeuralNetTrainingSessionDto>> StartTraining([FromBody] StartNeuralNetTrainingRequest request, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        return Accepted(await training.StartSyntheticSessionAsync(request, userId.Value, ct));
    }

    /// <summary>Lists recent synthetic sessions and replay availability.</summary>
    [HttpGet("training")]
    public async Task<ActionResult<IReadOnlyList<NeuralNetTrainingSessionDto>>> GetTrainingSessions(CancellationToken ct) =>
        Ok(await training.GetTrainingSessionsAsync(ct));

    /// <summary>Deletes a queued/completed session. Running sessions return 409 Conflict.</summary>
    [HttpDelete("training/{sessionId:guid}")]
    public async Task<IActionResult> RemoveTrainingSession(Guid sessionId, CancellationToken ct)
    {
        NeuralNetTrainingSessionRemovalResult result = await training.RemoveSessionAsync(sessionId, ct);
        return result switch
        {
            NeuralNetTrainingSessionRemovalResult.Removed => NoContent(),
            NeuralNetTrainingSessionRemovalResult.Running => Conflict(new { message = "A running training session cannot be removed." }),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// Downloads worker V2 replay JSON for <paramref name="chatMonitoringKind"/>, or legacy
    /// session report JSON when the kind query is omitted.
    /// </summary>
    [HttpGet("training/{sessionId:guid}/report")]
    public async Task<IActionResult> DownloadTrainingReport(Guid sessionId, [FromQuery] NeuralModelKindChatMonitoring? chatMonitoringKind, CancellationToken ct)
    {
        string? report = await training.GetSessionReportAsync(sessionId, chatMonitoringKind, ct);
        string kindSuffix = chatMonitoringKind is null ? string.Empty : $"-{chatMonitoringKind.Value.ToString().ToLowerInvariant()}";
        return string.IsNullOrWhiteSpace(report) ? NotFound() : File(System.Text.Encoding.UTF8.GetBytes(report), "application/json", $"neural-net-training-{sessionId:N}{kindSuffix}.json");
    }

    private Guid? GetUserId()
    {
        string? claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out Guid id) ? id : null;
    }
}
