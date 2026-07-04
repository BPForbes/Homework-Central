using System.Security.Claims;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace HomeworkCentral.Api.Hubs;

/// <summary>
/// <see cref="IChatMessageService.CanAccessRoomAsync"/> resolves the caller's effective mask via
/// <c>IEffectiveMaskService</c>, which — like most services in this app — reads the acting user's
/// tenant scope from an injected <see cref="IHttpContextAccessor"/> rather than taking a
/// <see cref="ClaimsPrincipal"/> parameter directly. That accessor's AsyncLocal-based flow is
/// reliable for the WebSocket transport in this single-server Kestrel deployment (verified via
/// direct SignalR client testing against persona accounts whose masks live in a tenant
/// database), but every hub method that calls into it explicitly re-binds
/// <see cref="IHttpContextAccessor.HttpContext"/> to this connection's own
/// <see cref="HubCallerContext.GetHttpContext"/> first, rather than trusting ambient
/// propagation — this is what Microsoft's own SignalR guidance recommends, and it also makes the
/// hub robust to any future move to a backplane/transport (e.g. Azure SignalR Service) where a
/// per-request AsyncLocal HttpContext may not be available at all inside a hub method.
/// </summary>
[Authorize]
public sealed class ChatHub(
    IChatMessageService chatMessageService,
    IChatTypingTracker typingTracker,
    IChatOnlineTracker onlineTracker,
    IHttpContextAccessor httpContextAccessor) : Hub
{
    public async Task JoinRoom(string roomId)
    {
        BindAmbientHttpContext();
        Guid userId = GetUserId();
        if (!await chatMessageService.CanAccessRoomAsync(roomId, userId))
            throw new HubException("You do not have access to this chat room.");

        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupKey);
        onlineTracker.UserJoined(groupKey, Context.ConnectionId, userId);

        // Live UserTyping events are broadcast only to OthersInGroup, so a user who joins (or
        // reconnects and re-joins) after someone started typing would otherwise miss them until
        // the next keystroke. Send a one-shot snapshot of current typers to the caller only.
        IReadOnlyList<ChatTypingDto> activeTypers = typingTracker.GetActiveTypers(groupKey, excludeUserId: userId);
        await Clients.Caller.SendAsync("TypingUsersSnapshot", activeTypers);
    }

    public async Task LeaveRoom(string roomId)
    {
        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        (string GroupKey, Guid UserId)? cleared = typingTracker.ClearTypingForConnection(Context.ConnectionId);
        if (cleared is not null && cleared.Value.GroupKey == groupKey)
            await Clients.OthersInGroup(groupKey).SendAsync("UserStoppedTyping", cleared.Value.UserId);

        onlineTracker.UserLeft(groupKey, Context.ConnectionId, GetUserId());
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupKey);
    }

    public async Task NotifyTyping(string roomId)
    {
        BindAmbientHttpContext();
        Guid userId = GetUserId();
        if (!await chatMessageService.CanAccessRoomAsync(roomId, userId))
            return;

        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        string username = Context.User?.FindFirst("username")?.Value ?? "User";
        typingTracker.SetTyping(Context.ConnectionId, groupKey, userId, username);

        ChatTypingDto payload = new() { UserId = userId, Username = username };
        await Clients.OthersInGroup(groupKey).SendAsync("UserTyping", payload);
    }

    public async Task NotifyStoppedTyping(string roomId)
    {
        BindAmbientHttpContext();
        Guid userId = GetUserId();
        if (!await chatMessageService.CanAccessRoomAsync(roomId, userId))
            return;

        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        if (typingTracker.ClearTyping(Context.ConnectionId, groupKey, userId))
            await Clients.OthersInGroup(groupKey).SendAsync("UserStoppedTyping", userId);
    }

    // Typing state has no server-side timeout (the indicator is meant to persist for as long
    // as the client reports text in the composer), so an abrupt disconnect must explicitly
    // clear it — otherwise a dropped connection would leave a "stuck" typing indicator for
    // everyone else in the room.
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        (string GroupKey, Guid UserId)? cleared = typingTracker.ClearTypingForConnection(Context.ConnectionId);
        if (cleared is not null)
            await Clients.OthersInGroup(cleared.Value.GroupKey).SendAsync("UserStoppedTyping", cleared.Value.UserId);

        onlineTracker.UserDisconnected(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>See the class-level remarks: makes IHttpContextAccessor-dependent services
    /// (reached via <see cref="chatMessageService"/>) resolve against this connection's own HTTP
    /// context rather than relying on ambient AsyncLocal propagation into the hub dispatch.</summary>
    private void BindAmbientHttpContext()
    {
        HttpContext? connectionHttpContext = Context.GetHttpContext();
        if (connectionHttpContext is not null)
            httpContextAccessor.HttpContext = connectionHttpContext;
    }

    private Guid GetUserId()
    {
        string? userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out Guid userId))
            throw new HubException("Unauthorized.");

        return userId;
    }
}
