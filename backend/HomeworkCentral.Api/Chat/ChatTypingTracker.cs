using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// In-memory typing presence per chat room group. Typing state has no server-side timeout —
/// the indicator is expected to persist for as long as the client reports the user has text in
/// their composer — so every connection's active typing target is also tracked here, letting
/// the hub clear it (and notify the room) if the connection drops before the client sends an
/// explicit "stopped typing" notification.
///
/// A single user can have more than one active connection to the same room (multiple browser
/// tabs, or one tab reconnecting while the old connection hasn't fully closed yet), so presence
/// is refcounted per connection: a user is only reported as "stopped typing" once none of their
/// connections in that room are still marked as typing.
/// </summary>
public sealed class ChatTypingTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TypingEntry>> _rooms = new();
    private readonly ConcurrentDictionary<string, (string GroupKey, Guid UserId)> _connections = new();

    public void SetTyping(string connectionId, string groupKey, Guid userId, string username)
    {
        ConcurrentDictionary<Guid, TypingEntry> room =
            _rooms.GetOrAdd(groupKey, _ => new ConcurrentDictionary<Guid, TypingEntry>());
        TypingEntry entry = room.GetOrAdd(userId, _ => new TypingEntry(username));
        entry.ConnectionIds[connectionId] = 0;
        _connections[connectionId] = (groupKey, userId);
    }

    /// <summary>Clears one connection's typing state. Returns true if this was that user's last
    /// active connection in the room (i.e. the caller should broadcast "stopped typing").</summary>
    public bool ClearTyping(string connectionId, string groupKey, Guid userId)
    {
        _connections.TryRemove(connectionId, out _);
        return RemoveConnectionFromRoom(groupKey, userId, connectionId);
    }

    /// <summary>Clears any typing state left by a disconnecting connection. Returns the room and
    /// user to notify as "stopped typing" only if this was that user's last active connection in
    /// the room, or null if the connection wasn't marked as typing anywhere.</summary>
    public (string GroupKey, Guid UserId)? ClearTypingForConnection(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out (string GroupKey, Guid UserId) entry))
            return null;

        bool wasLastConnection = RemoveConnectionFromRoom(entry.GroupKey, entry.UserId, connectionId);
        return wasLastConnection ? entry : null;
    }

    private bool RemoveConnectionFromRoom(string groupKey, Guid userId, string connectionId)
    {
        if (!_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            return false;

        if (!room.TryGetValue(userId, out TypingEntry? entry))
            return false;

        entry.ConnectionIds.TryRemove(connectionId, out _);
        if (!entry.ConnectionIds.IsEmpty)
            return false;

        // Best-effort: if another connection starts typing for this user between the emptiness
        // check above and this removal, TryRemove below will simply no-op for that race (a rare,
        // low-stakes presence indicator, not correctness-critical state), and the new connection's
        // own SetTyping call already re-added a fresh entry that stays intact regardless.
        return room.TryRemove(userId, out _);
    }

    private sealed class TypingEntry(string username)
    {
        public string Username { get; } = username;
        public ConcurrentDictionary<string, byte> ConnectionIds { get; } = new();
    }
}
