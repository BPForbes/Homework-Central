using HomeworkCentral.Api.DTOs;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// Tracks who is currently typing in each chat room group. The default in-process implementation
/// is acceptable for single-server dev; horizontal scaling would require a distributed backing
/// store (e.g. Redis via <c>IDistributedCache</c>) and a SignalR backplane.
/// </summary>
public interface IChatTypingTracker
{
    void SetTyping(string connectionId, string groupKey, Guid userId, string username);

    /// <summary>Returns true when this was the user's last active connection in the room.</summary>
    bool ClearTyping(string connectionId, string groupKey, Guid userId);

    (string GroupKey, Guid UserId)? ClearTypingForConnection(string connectionId);

    IReadOnlyList<ChatTypingDto> GetActiveTypers(string groupKey, Guid? excludeUserId = null);
}
