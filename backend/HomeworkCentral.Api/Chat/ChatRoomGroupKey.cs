using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Tenancy;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// SignalR group names scoped by room and a real-vs-developer traffic bucket.
/// Chat rooms are shared community spaces (see <see cref="ChatRoomAccessService"/>), not
/// per-tenant private data, so the group key intentionally ignores the caller's tenant
/// database: each dev persona provisions its own isolated tenant, and grouping by exact
/// tenant would mean no two personas could ever land in the same room together.
/// </summary>
public static class ChatRoomGroupKey
{
    public static string Build(string roomId, AccountClass accountClass) =>
        $"chat:{roomId}:{Bucket(accountClass)}";

    public static string Build(System.Security.Claims.ClaimsPrincipal user, string roomId) =>
        Build(roomId, ResolveAccountClass(user));

    private static AccountClass ResolveAccountClass(System.Security.Claims.ClaimsPrincipal user)
    {
        string? accountClassClaim = user.FindFirst(TenancyConstants.AccountClassClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(accountClassClaim)
            && Enum.TryParse(accountClassClaim, ignoreCase: false, out AccountClass parsed))
        {
            return parsed;
        }

        return AccountClass.RealAccount;
    }

    private static string Bucket(AccountClass accountClass) =>
        accountClass == AccountClass.RealAccount ? "real" : "dev";
}
