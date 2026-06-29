using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/auth/dev")]
public class DevAuthController(
    IAuthService auth,
    IConfiguration config,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        if (!CanUseDevBypass())
            return NotFound();

        DevLoginOptionsResponse options = await auth.GetDevLoginOptionsAsync();
        return Ok(options);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] DevLoginRequest req)
    {
        if (!CanUseDevBypass())
            return NotFound();

        try
        {
            AuthResponse result = await auth.DevLoginAsync(req);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!CanUseDevBypass())
            return NotFound();

        return Ok(new { available = true });
    }

    private bool CanUseDevBypass() =>
        DevBypass.IsEnabled(config, env) && DevBypass.IsLocalhost(HttpContext);
}
