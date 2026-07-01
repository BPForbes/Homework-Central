using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// In-memory typing presence per chat room group. Typing state has no server-side timeout —
/// the indicator is expected to persist for as long as the client reports the user has text in
/// their composer — so every connection's active typing target is also tracked here, letting
/// the hub clear it (and notify the room) if the connection drops before the client sends an
/// explicit "stopped typing" notification.
/// </summary>
public sealed class ChatTypingTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TypingEntry>> _rooms = new();
    private readonly ConcurrentDictionary<string, (string GroupKey, Guid UserId)> _connections = new();

    public void SetTyping(string connectionId, string groupKey, Guid userId, string username)
    {
        ConcurrentDictionary<Guid, TypingEntry> room =
            _rooms.GetOrAdd(groupKey, _ => new ConcurrentDictionary<Guid, TypingEntry>());
        room[userId] = new TypingEntry(username, DateTime.UtcNow);
        _connections[connectionId] = (groupKey, userId);
    }

    public void ClearTyping(string connectionId, string groupKey, Guid userId)
    {
        if (_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            room.TryRemove(userId, out _);

        _connections.TryRemove(connectionId, out _);
    }

    /// <summary>Clears any typing state left by a disconnecting connection. Returns the room and
    /// user that should be notified as "stopped typing", or null if the connection wasn't
    /// currently marked as typing anywhere.</summary>
    public (string GroupKey, Guid UserId)? ClearTypingForConnection(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out (string GroupKey, Guid UserId) entry))
            return null;

        if (_rooms.TryGetValue(entry.GroupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            room.TryRemove(entry.UserId, out _);

        return entry;
    }

    private sealed record TypingEntry(string Username, DateTime UpdatedAtUtc);
}
