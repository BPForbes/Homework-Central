using HomeworkCentral.Api.Chat.Mentions;

namespace HomeworkCentral.Api.Tests.Chat;

public class MentionParserTests
{
    [Fact]
    public void Escaped_mention_renders_as_plain_text_without_active_mentions()
    {
        MentionParseResult result = MentionParser.Parse("Hello ``@alice`` there", canUseBroadcastMentions: true);

        Assert.Equal("Hello @alice there", result.DisplayContent);
        Assert.Empty(result.ActiveMentions);
        Assert.False(result.ContainsAnyMentionToken);
    }

    [Fact]
    public void Active_user_mention_is_parsed()
    {
        MentionParseResult result = MentionParser.Parse("Hi @bob", canUseBroadcastMentions: false);

        Assert.Equal("Hi @bob", result.DisplayContent);
        Assert.Single(result.ActiveMentions);
        Assert.Equal(MentionKind.User, result.ActiveMentions[0].Kind);
        Assert.Equal("bob", result.ActiveMentions[0].Token);
        Assert.True(result.ActiveMentions[0].IsActive);
    }

    [Fact]
    public void Unauthorized_everyone_becomes_null_plain_text()
    {
        MentionParseResult result = MentionParser.Parse("Ping @everyone", canUseBroadcastMentions: false);

        Assert.Equal("Ping @null", result.DisplayContent);
        Assert.Single(result.ActiveMentions);
        Assert.Equal(MentionKind.Everyone, result.ActiveMentions[0].Kind);
        Assert.False(result.ActiveMentions[0].IsActive);
    }

    [Fact]
    public void Owner_can_use_everyone_and_here()
    {
        MentionParseResult everyone = MentionParser.Parse("@everyone", canUseBroadcastMentions: true);
        MentionParseResult here = MentionParser.Parse("@here", canUseBroadcastMentions: true);

        Assert.Equal("@everyone", everyone.DisplayContent);
        Assert.True(everyone.ActiveMentions[0].IsActive);
        Assert.Equal(MentionKind.Everyone, everyone.ActiveMentions[0].Kind);

        Assert.Equal("@here", here.DisplayContent);
        Assert.True(here.ActiveMentions[0].IsActive);
        Assert.Equal(MentionKind.Here, here.ActiveMentions[0].Kind);
    }

    [Fact]
    public void Role_mention_is_classified()
    {
        MentionParseResult result = MentionParser.Parse("Hey @Tutor", canUseBroadcastMentions: false);

        Assert.Single(result.ActiveMentions);
        Assert.Equal(MentionKind.Role, result.ActiveMentions[0].Kind);
        Assert.Equal("Tutor", result.ActiveMentions[0].Token);
    }
}
