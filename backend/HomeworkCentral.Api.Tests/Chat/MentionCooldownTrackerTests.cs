using HomeworkCentral.Api.Chat.Mentions;

namespace HomeworkCentral.Api.Tests.Chat;

public class MentionCooldownTrackerTests
{
    [Fact]
    public void First_mention_is_allowed()
    {
        MentionCooldownTracker tracker = new();
        Guid userId = Guid.NewGuid();

        Assert.True(tracker.TryRecordMention(userId, TimeSpan.FromSeconds(3), out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void Second_mention_within_cooldown_is_rejected()
    {
        MentionCooldownTracker tracker = new();
        Guid userId = Guid.NewGuid();
        TimeSpan cooldown = TimeSpan.FromSeconds(3);

        Assert.True(tracker.TryRecordMention(userId, cooldown, out _));
        Assert.False(tracker.TryRecordMention(userId, cooldown, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Mention_is_allowed_again_after_cooldown_expires()
    {
        MentionCooldownTracker tracker = new();
        Guid userId = Guid.NewGuid();
        TimeSpan cooldown = TimeSpan.FromMilliseconds(40);

        Assert.True(tracker.TryRecordMention(userId, cooldown, out _));
        Thread.Sleep(50);
        Assert.True(tracker.TryRecordMention(userId, cooldown, out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void Cooldowns_are_isolated_per_user()
    {
        MentionCooldownTracker tracker = new();
        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();
        TimeSpan cooldown = TimeSpan.FromSeconds(3);

        Assert.True(tracker.TryRecordMention(alice, cooldown, out _));
        Assert.True(tracker.TryRecordMention(bob, cooldown, out _));
    }

    [Fact]
    public void Concurrent_mentions_do_not_bypass_cooldown()
    {
        MentionCooldownTracker tracker = new();
        Guid userId = Guid.NewGuid();
        TimeSpan cooldown = TimeSpan.FromSeconds(3);
        int allowed = 0;

        Parallel.For(0, 8, _loopIndex =>
        {
            if (tracker.TryRecordMention(userId, cooldown, out _))
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(1, allowed);
    }
}
