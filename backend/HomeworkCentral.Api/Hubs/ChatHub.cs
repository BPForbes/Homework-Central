using System.Security.Claims;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HomeworkCentral.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    IChatMessageService chatMessageService,
    ChatTypingTracker typingTracker) : Hub
{
    public async Task JoinRoom(string roomId)
    {
        Guid userId = GetUserId();
        if (!await chatMessageService.CanAccessRoomAsync(roomId, userId))
            throw new HubException("You do not have access to this chat room.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatRoomGroupKey.Build(Context.User!, roomId));
    }

    public async Task LeaveRoom(string roomId)
    {
        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        (string GroupKey, Guid UserId)? cleared = typingTracker.ClearTypingForConnection(Context.ConnectionId);
        if (cleared is not null && cleared.Value.GroupKey == groupKey)
            await Clients.OthersInGroup(groupKey).SendAsync("UserStoppedTyping", cleared.Value.UserId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupKey);
    }

    public async Task NotifyTyping(string roomId)
    {
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
        Guid userId = GetUserId();
        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        typingTracker.ClearTyping(Context.ConnectionId, groupKey, userId);
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

        await base.OnDisconnectedAsync(exception);
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
