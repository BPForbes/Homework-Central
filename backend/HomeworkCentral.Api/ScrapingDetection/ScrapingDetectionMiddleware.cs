using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace HomeworkCentral.Api.ScrapingDetection;

/// <summary>
/// Runs after authentication/authorization (so <c>context.User</c> is populated when present) and
/// before routing, recording every <c>/api/*</c> request — captcha, chat, subjects, auth, roles,
/// everything — with <see cref="IScrapingDetectionService"/> and short-circuiting with 429 once an
/// identity's request pattern crosses the scraping-suspicion threshold. Health checks and the
/// SignalR hub connection aren't under <c>/api</c>, so they're unaffected.
/// </summary>
public sealed class ScrapingDetectionMiddleware(RequestDelegate next, IScrapingDetectionService detector, ILogger<ScrapingDetectionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        string identity = ResolveIdentity(context);
        ScrapingRequestSample sample = new(identity, context.Request.Path.Value ?? "/", context.Request.Method, DateTime.UtcNow);
        ScrapingAssessment assessment = detector.RecordRequest(sample);

        if (assessment.ShouldBlock)
        {
            logger.LogWarning(
                "Blocked possible data scraping from {Identity}: score {Score:F2} ({Reason})",
                identity,
                assessment.SuspicionScore,
                assessment.Reason);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = "30";
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Please slow down." });
            return;
        }

        await next(context);
    }

    private static string ResolveIdentity(HttpContext context)
    {
        string? userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
            return $"user:{userId}";

        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}
