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

    public Task LeaveRoom(string roomId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatRoomGroupKey.Build(Context.User!, roomId));

    public async Task NotifyTyping(string roomId)
    {
        Guid userId = GetUserId();
        if (!await chatMessageService.CanAccessRoomAsync(roomId, userId))
            return;

        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        string username = Context.User?.FindFirst("username")?.Value ?? "User";
        typingTracker.SetTyping(groupKey, userId, username);

        ChatTypingDto payload = new() { UserId = userId, Username = username };
        await Clients.OthersInGroup(groupKey).SendAsync("UserTyping", payload);
    }

    public async Task NotifyStoppedTyping(string roomId)
    {
        Guid userId = GetUserId();
        string groupKey = ChatRoomGroupKey.Build(Context.User!, roomId);
        typingTracker.ClearTyping(groupKey, userId);
        await Clients.OthersInGroup(groupKey).SendAsync("UserStoppedTyping", userId);
    }

    public override Task OnDisconnectedAsync(Exception? exception) =>
        base.OnDisconnectedAsync(exception);

    private Guid GetUserId()
    {
        string? userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out Guid userId))
            throw new HubException("Unauthorized.");

        return userId;
    }
}
