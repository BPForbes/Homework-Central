using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

/// <summary>
/// Localhost-only developer bypass auth endpoints. Hidden (404) unless
/// <see cref="DevBypass.IsEnabled"/> and the caller is on loopback.
/// </summary>
[ApiController]
[Route("api/auth/dev")]
public class DevAuthController(
    IAuthService auth,
    IConfiguration config,
    IWebHostEnvironment env,
    IServiceProvider serviceProvider) : ControllerBase
{
    private IDevPersonaProvisioner? PersonaProvisioner =>
        serviceProvider.GetService<IDevPersonaProvisioner>();

    /// <summary>Returns developer accounts and personas for the /devlogin dropdowns.</summary>
    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        if (!CanUseDevBypass())
            return NotFound();

        DevLoginOptionsResponse options = await auth.GetDevLoginOptionsAsync();
        return Ok(options);
    }

    /// <summary>Signs in as the selected persona, or DevAdmin when no persona is chosen.</summary>
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

    /// <summary>Lets the frontend probe whether dev bypass endpoints are reachable.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        if (!CanUseDevBypass())
            return NotFound();

        if (PersonaProvisioner is null)
            return Ok(new { available = true });

        return Ok(new
        {
            available = true,
            personasProvisioned = PersonaProvisioner.ProvisionedCount,
            personasTotal = PersonaProvisioner.TotalPersonaCount,
            // "Ready" means no pending background sweep. In on-demand mode (the default) every
            // persona is selectable immediately — each provisions at its first dev login — so
            // the login page must not wait on a count that only the eager sweep advances.
            personasReady = !DevPersonaEagerProvisioning.IsEnabled(config)
                || PersonaProvisioner.ProvisionedCount >= PersonaProvisioner.TotalPersonaCount,
        });
    }

    private bool CanUseDevBypass() =>
        DevBypass.IsEnabled(config, env) && DevBypass.IsLocalhost(HttpContext);
}
