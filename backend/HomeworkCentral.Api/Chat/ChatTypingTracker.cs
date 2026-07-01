using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Chat;

/// <summary>In-memory typing presence per chat room group.</summary>
public sealed class ChatTypingTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, TypingEntry>> _rooms = new();

    public void SetTyping(string groupKey, Guid userId, string username)
    {
        ConcurrentDictionary<Guid, TypingEntry> room =
            _rooms.GetOrAdd(groupKey, _ => new ConcurrentDictionary<Guid, TypingEntry>());
        room[userId] = new TypingEntry(username, DateTime.UtcNow);
    }

    public void ClearTyping(string groupKey, Guid userId)
    {
        if (_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            room.TryRemove(userId, out _);
    }

    public IReadOnlyList<(Guid UserId, string Username)> GetTypingUsers(string groupKey, Guid excludeUserId, TimeSpan maxAge)
    {
        if (!_rooms.TryGetValue(groupKey, out ConcurrentDictionary<Guid, TypingEntry>? room))
            return [];

        DateTime cutoff = DateTime.UtcNow - maxAge;
        List<(Guid UserId, string Username)> active = new();

        foreach ((Guid userId, TypingEntry entry) in room)
        {
            if (userId == excludeUserId)
                continue;

            if (entry.UpdatedAtUtc < cutoff)
            {
                room.TryRemove(userId, out _);
                continue;
            }

            active.Add((userId, entry.Username));
        }

        return active;
    }

    private sealed record TypingEntry(string Username, DateTime UpdatedAtUtc);
}
