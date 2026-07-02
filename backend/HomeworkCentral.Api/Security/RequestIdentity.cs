using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace HomeworkCentral.Api.Security;

/// <summary>
/// Single definition of "who is making this request" shared by every per-identity tracker
/// (scraping detection, captcha risk profiles, ...): <c>user:{userId}</c> for authenticated
/// callers, else <c>ip:{address}</c>, else <c>ip:unknown</c> when neither is resolvable.
/// </summary>
public static class RequestIdentity
{
    public static string Resolve(HttpContext? context)
    {
        if (context is null)
            return "ip:unknown";

        string? userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
            return $"user:{userId}";

        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}
