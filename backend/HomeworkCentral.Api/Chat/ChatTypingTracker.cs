using System.Collections.Concurrent;
using HomeworkCentral.Api.DTOs;

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
///
/// State is in-memory only and is lost on server restart. For multi-instance deployments, swap
/// this implementation for one backed by a shared store and register a SignalR backplane.
/// </summary>
public sealed class ChatTypingTracker : IChatTypingTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TypingEntry>> _rooms = new();
    private readonly ConcurrentDictionary<string, (string GroupKey, Guid UserId)> _connections = new();

    public void SetTyping(string connectionId, string groupKey, Guid userId, string username)
    {
        // A connection can only be actively typing in one room at a time, but a prior NotifyTyping
        // in another room is not automatically cleared when the client moves rooms (e.g. a reconnect
        // that re-JoinRoom's without LeaveRoom first). Drop any stale room entry before updating.
        if (_connections.TryGetValue(connectionId, out (string GroupKey, Guid UserId) prior)
            && (prior.GroupKey != groupKey || prior.UserId != userId))
        {
            RemoveConnectionFromRoom(prior.GroupKey, prior.UserId, connectionId);
        }

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
        _connections.TryRemove(connectionId, out _);

        // Sweep every room: a connection can leak into a room if SetTyping moved rooms without
        // clearing the old entry (fixed above, but this also cleans up state left by older builds).
        (string GroupKey, Guid UserId)? notify = null;
        IEnumerable<(string GroupKey, Guid UserId)> connectionRooms = _rooms.ToArray()
            .SelectMany(roomPair => roomPair.Value.ToArray()
                .Where(userPair => userPair.Value.ConnectionIds.ContainsKey(connectionId))
                .Select(userPair => (GroupKey: roomPair.Key, UserId: userPair.Key)));

        foreach ((string groupKey, Guid userId) in connectionRooms)
        {
            if (RemoveConnectionFromRoom(groupKey, userId, connectionId))
                notify = (groupKey, userId);
        }

        return notify;
    }

    /// <summary>Returns every user currently marked as typing in <paramref name="groupKey"/>,
    /// optionally omitting <paramref name="excludeUserId"/> (used when a new connection joins so
    /// the caller never sees their own indicator echoed back).</summary>
    public IReadOnlyList<ChatTypingDto> GetActiveTypers(string groupKey, Guid? excludeUserId = null)
    {
        if (!_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            return [];

        return room
            .Where(kvp => excludeUserId is null || kvp.Key != excludeUserId.Value)
            .Select(kvp => new ChatTypingDto { UserId = kvp.Key, Username = kvp.Value.Username })
            .ToList();
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
