using HomeworkCentral.Api.Chat;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatTypingTrackerTests
{
    private const string GroupKey = "chat:general:lobby:real";

    [Fact]
    public void GetActiveTypers_returns_empty_when_room_has_no_typers()
    {
        var tracker = new ChatTypingTracker();

        Assert.Empty(tracker.GetActiveTypers(GroupKey));
    }

    [Fact]
    public void GetActiveTypers_returns_users_currently_typing_in_the_room()
    {
        var tracker = new ChatTypingTracker();
        var aliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        tracker.SetTyping("conn-alice", GroupKey, aliceId, "Alice");
        tracker.SetTyping("conn-bob", GroupKey, bobId, "Bob");

        var typers = tracker.GetActiveTypers(GroupKey);

        Assert.Equal(2, typers.Count);
        Assert.Contains(typers, t => t.UserId == aliceId && t.Username == "Alice");
        Assert.Contains(typers, t => t.UserId == bobId && t.Username == "Bob");
    }

    [Fact]
    public void GetActiveTypers_excludes_requested_user()
    {
        var tracker = new ChatTypingTracker();
        var aliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        tracker.SetTyping("conn-alice", GroupKey, aliceId, "Alice");
        tracker.SetTyping("conn-bob", GroupKey, bobId, "Bob");

        var typers = tracker.GetActiveTypers(GroupKey, excludeUserId: aliceId);

        Assert.Single(typers);
        Assert.Equal(bobId, typers[0].UserId);
    }

    [Fact]
    public void GetActiveTypers_omits_user_after_last_connection_stops_typing()
    {
        var tracker = new ChatTypingTracker();
        var aliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        tracker.SetTyping("conn-alice", GroupKey, aliceId, "Alice");
        Assert.True(tracker.ClearTyping("conn-alice", GroupKey, aliceId));

        Assert.Empty(tracker.GetActiveTypers(GroupKey));
    }

    [Fact]
    public void GetActiveTypers_keeps_user_while_another_connection_is_still_typing()
    {
        var tracker = new ChatTypingTracker();
        var aliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        tracker.SetTyping("conn-alice-tab1", GroupKey, aliceId, "Alice");
        tracker.SetTyping("conn-alice-tab2", GroupKey, aliceId, "Alice");
        Assert.False(tracker.ClearTyping("conn-alice-tab1", GroupKey, aliceId));

        var typers = tracker.GetActiveTypers(GroupKey);

        Assert.Single(typers);
        Assert.Equal(aliceId, typers[0].UserId);
    }
}
