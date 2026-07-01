using System.Security.Claims;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/captcha")]
public class CaptchaController(ICaptchaService captcha, ICaptchaRoleService captchaRoles) : ControllerBase
{
    /// <summary>Issues a new text captcha challenge. Used by both the signup form and the
    /// dashboard "Verify" button, so it is reachable without authentication.</summary>
    [HttpGet("challenge")]
    public ActionResult<CaptchaChallengeDto> GetChallenge() => Ok(captcha.CreateChallenge());

    /// <summary>Dashboard "Verify" button: solving the captcha strips Guest (if present) and
    /// grants VerifiedUser to the current user.</summary>
    [HttpPost("verify-role")]
    [Authorize]
    public async Task<IActionResult> VerifyRole([FromBody] CaptchaVerifyRequest request, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool verified = await captchaRoles.TryVerifyAndPromoteAsync(userId.Value, request.ChallengeId, request.Answer, ct);
        if (!verified)
            return BadRequest(new { error = "That wasn't correct. Try the new code below." });

        return NoContent();
    }

    private Guid? GetUserId()
    {
        string? sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(sub, out Guid userId) ? userId : null;
    }
}
