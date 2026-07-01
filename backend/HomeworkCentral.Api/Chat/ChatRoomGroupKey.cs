using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Tenancy;

namespace HomeworkCentral.Api.Chat;

/// <summary>SignalR group names scoped by room, account class, and tenant database.</summary>
public static class ChatRoomGroupKey
{
    public static string Build(string roomId, AccountClass accountClass, string? tenantDatabaseName) =>
        $"chat:{roomId}:{accountClass}:{tenantDatabaseName ?? "master"}";

    public static string Build(System.Security.Claims.ClaimsPrincipal user, string roomId)
    {
        AccountClass accountClass = AccountClass.RealAccount;
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(accountClassClaim)
            && Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass parsed))
        {
            accountClass = parsed;
        }

        string? tenantDatabaseName = user.FindFirst(TenancyConstants.TenantDbClaimName)?.Value;
        return Build(roomId, accountClass, string.IsNullOrWhiteSpace(tenantDatabaseName) ? null : tenantDatabaseName);
    }
}
