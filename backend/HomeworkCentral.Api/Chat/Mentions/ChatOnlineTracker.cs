using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Chat.Mentions;

public interface IChatOnlineTracker
{
    void UserJoined(string groupKey, string connectionId, Guid userId);
    void UserLeft(string groupKey, string connectionId, Guid userId);
    void UserDisconnected(string connectionId);
    IReadOnlyCollection<Guid> GetOnlineUserIds(string groupKey);
}

/// <summary>
/// Tracks which users are connected to a chat room group (for @here mentions).
/// </summary>
public sealed class ChatOnlineTracker : IChatOnlineTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ConnectionSet>> _rooms = new();
    private readonly ConcurrentDictionary<string, (string GroupKey, Guid UserId)> _connections = new();

    public void UserJoined(string groupKey, string connectionId, Guid userId)
    {
        ConcurrentDictionary<Guid, ConnectionSet> room =
            _rooms.GetOrAdd(groupKey, _ => new ConcurrentDictionary<Guid, ConnectionSet>());
        room.GetOrAdd(userId, _ => new ConnectionSet()).ConnectionIds[connectionId] = 0;
        _connections[connectionId] = (groupKey, userId);
    }

    public void UserLeft(string groupKey, string connectionId, Guid userId)
    {
        _connections.TryRemove(connectionId, out _);
        RemoveConnection(groupKey, userId, connectionId);
    }

    public void UserDisconnected(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out (string GroupKey, Guid UserId) entry))
            return;

        RemoveConnection(entry.GroupKey, entry.UserId, connectionId);
    }

    public IReadOnlyCollection<Guid> GetOnlineUserIds(string groupKey)
    {
        if (!_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, ConnectionSet>? room))
            return [];

        return room
            .Where(entry => !entry.Value.ConnectionIds.IsEmpty)
            .Select(entry => entry.Key)
            .ToArray();
    }

    private void RemoveConnection(string groupKey, Guid userId, string connectionId)
    {
        if (!_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, ConnectionSet>? room))
            return;

        if (!room.TryGetValue(userId, out ConnectionSet? entry))
            return;

        entry.ConnectionIds.TryRemove(connectionId, out _);
    }

    private sealed class ConnectionSet
    {
        public ConcurrentDictionary<string, byte> ConnectionIds { get; } = new();
    }
}
