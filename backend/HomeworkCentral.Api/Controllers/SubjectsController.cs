using System.Security.Claims;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

/// <summary>Backs the "Get Roles" room: self-service claiming of general subjects
/// (Mathematics, Science, Computer Science, ...). Not staff-specific — no ManageRoles needed.</summary>
[ApiController]
[Route("api/subjects")]
[Authorize]
public class SubjectsController(ISubjectClaimService subjectClaims) : ControllerBase
{
    [HttpGet("general")]
    public async Task<ActionResult<IReadOnlyList<ClaimableSubjectDto>>> GetGeneralSubjects(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await subjectClaims.GetClaimableSubjectsAsync(userId.Value, ct));
    }

    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] ClaimSubjectRequest request, CancellationToken ct)
    {
        IActionResult? validation = ValidateRequest(request, out Guid userId);
        if (validation is not null)
            return validation;

        try
        {
            await subjectClaims.ClaimSubjectAsync(userId, request.SubjectName, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("unclaim")]
    public async Task<IActionResult> Unclaim([FromBody] ClaimSubjectRequest request, CancellationToken ct)
    {
        IActionResult? validation = ValidateRequest(request, out Guid userId);
        if (validation is not null)
            return validation;

        try
        {
            await subjectClaims.UnclaimSubjectAsync(userId, request.SubjectName, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private IActionResult? ValidateRequest(ClaimSubjectRequest request, out Guid userId)
    {
        userId = default;

        if (string.IsNullOrWhiteSpace(request.SubjectName))
            return new BadRequestObjectResult(new { error = "SubjectName is required." });

        Guid? resolved = GetUserId();
        if (resolved is null)
            return Unauthorized();

        userId = resolved.Value;
        return null;
    }

    private Guid? GetUserId()
    {
        string? sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(sub, out Guid userId) ? userId : null;
    }
}
