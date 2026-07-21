using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/neural-net")]
[Authorize(Policy = AuthorizationPolicyNames.ManageServerInfrastructure)]
public sealed class NeuralNetController(INeuralNetTrainingService training) : ControllerBase
{
    [HttpGet("training-feedback")]
    public async Task<ActionResult<IReadOnlyList<NeuralNetTrainingFeedbackDto>>> GetTrainingFeedback(CancellationToken ct) =>
        Ok(await training.GetPendingFeedbackAsync(ct));

    [HttpPost("training-feedback/{scoreEventId:guid}/approve")]
    public async Task<ActionResult<NeuralNetTrainingFeedbackDto>> Approve(Guid scoreEventId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        try { return Ok(await training.ApproveAsync(scoreEventId, userId.Value, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("training-feedback/{scoreEventId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid scoreEventId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        try { await training.RejectAsync(scoreEventId, userId.Value, ct); return NoContent(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("data-management")]
    public async Task<ActionResult<NeuralNetDataManagementDto>> GetDataManagement(CancellationToken ct) =>
        Ok(await training.GetDataManagementAsync(ct));

    [HttpGet("visualizer")]
    public async Task<ActionResult<NeuralNetVisualizerDto>> GetVisualizer(CancellationToken ct) =>
        Ok(await training.GetVisualizerAsync(ct));

    [HttpPost("training")]
    public async Task<ActionResult<NeuralNetTrainingSessionDto>> StartTraining([FromBody] StartNeuralNetTrainingRequest request, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null) return Unauthorized();
        return Accepted(await training.StartSyntheticSessionAsync(request, userId.Value, ct));
    }

    [HttpGet("training")]
    public async Task<ActionResult<IReadOnlyList<NeuralNetTrainingSessionDto>>> GetTrainingSessions(CancellationToken ct) =>
        Ok(await training.GetTrainingSessionsAsync(ct));

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
