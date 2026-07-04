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
}
