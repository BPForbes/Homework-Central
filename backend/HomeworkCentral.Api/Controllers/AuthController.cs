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
            AuthResponse result = await auth.RegisterAsync(req);
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
            AuthResponse result = await auth.LoginAsync(req);
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

        string? token = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { message = "No refresh token." });

        try
        {
            AuthResponse result = await auth.RefreshAsync(token);
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

        string? token = Request.Cookies["refresh_token"];
        if (!string.IsNullOrEmpty(token))
            await auth.RevokeRefreshTokenAsync(token);

        CookieOptions cookieOptions = new()
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
        };

        Response.Cookies.Delete("refresh_token", cookieOptions);
        Response.Cookies.Delete("tenant_db", cookieOptions);

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        string? sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out Guid userId))
            return Unauthorized();

        string? tenantDb = User.FindFirstValue("tenant_db");
        UserDto? user = await auth.GetCurrentUserAsync(userId, tenantDb);
        if (user is null) return NotFound();

        return Ok(user);
    }

    // Validates Origin header against the configured frontend origin for CSRF defense-in-depth.
    // SameSite=Strict is the primary protection; this is a secondary layer.
    private bool IsValidOrigin()
    {
        string origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true; // Same-origin or non-browser requests don't send Origin
        string allowedOrigin = config["Cors:AllowedOrigin"] ?? "http://localhost:5173";
        return origin == allowedOrigin;
    }
}
