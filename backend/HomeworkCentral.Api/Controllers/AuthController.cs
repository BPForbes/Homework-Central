using System.Security.Claims;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, IConfiguration config) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        try
        {
            var result = await auth.RegisterAsync(req);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        try
        {
            var result = await auth.LoginAsync(req);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!IsValidOrigin())
            return Forbid();

        var token = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { message = "No refresh token." });

        try
        {
            var result = await auth.RefreshAsync(token);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!IsValidOrigin())
            return Forbid();

        var token = Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(token))
            await auth.RevokeRefreshTokenAsync(token);

        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
        });

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var user = await auth.GetCurrentUserAsync(userId);
        if (user is null) return NotFound();

        return Ok(user);
    }

    // Validates Origin header against the configured frontend origin for CSRF defense-in-depth.
    // SameSite=Strict is the primary protection; this is a secondary layer.
    private bool IsValidOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true; // Same-origin or non-browser requests don't send Origin
        var allowedOrigin = config["Cors:AllowedOrigin"] ?? "http://localhost:5173";
        return origin == allowedOrigin;
    }
}
