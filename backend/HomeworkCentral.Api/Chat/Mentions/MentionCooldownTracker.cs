using System.Collections.Concurrent;

namespace HomeworkCentral.Api.Chat.Mentions;

public interface IMentionCooldownTracker
{
    bool TryRecordMention(Guid userId, TimeSpan cooldown, out TimeSpan retryAfter);
}

/// <summary>
/// In-memory per-user mention cooldown. Senior staff bypass this check in the caller.
/// </summary>
public sealed class MentionCooldownTracker : IMentionCooldownTracker
{
    private readonly ConcurrentDictionary<Guid, DateTime> _lastMentionUtc = new();

    public bool TryRecordMention(Guid userId, TimeSpan cooldown, out TimeSpan retryAfter)
    {
        DateTime now = DateTime.UtcNow;
        DateTime allowedAfter = _lastMentionUtc.AddOrUpdate(
            userId,
            _ => now,
            (_, lastUtc) =>
            {
                if (now - lastUtc >= cooldown)
                    return now;
                return lastUtc;
            });

        if (allowedAfter == now)
        {
            retryAfter = TimeSpan.Zero;
            return true;
        }

        retryAfter = cooldown - (now - allowedAfter);
        if (retryAfter < TimeSpan.Zero)
            retryAfter = TimeSpan.Zero;

        return false;
    }
}
