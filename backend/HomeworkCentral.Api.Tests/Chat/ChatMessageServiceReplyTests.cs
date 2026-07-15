using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeworkCentral.Api.Tests.Chat;

/// <summary>
/// Real-Postgres coverage for the reply-to-message feature: reply metadata denormalized onto the
/// reply row, and the "reply notifies the original sender" tie-in to the mention/inbox system.
/// </summary>
[Collection(nameof(ChatPostgresTestCollection))]
public class ChatMessageServiceReplyTests : IAsyncLifetime
{
    private readonly string _connectionString = ChatMessageServiceTestSupport.ResolveConnectionString();
    private bool _databaseAvailable;
    private AppDbContext _db = null!;
    private readonly string _roomId = ChatRoomCatalog.GeneralRoom.Id;

    public async Task InitializeAsync()
    {
        _databaseAvailable = ChatMessageServiceTestSupport.CanConnect(_connectionString);
        if (!_databaseAvailable)
            return;

        _db = await ChatMessageServiceTestSupport.CreateMigratedDatabaseAsync(_connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_databaseAvailable)
            await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Reply_denormalizes_parent_sender_and_a_content_snippet()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        ChatMessageDto? original = await ChatMessageServiceTestSupport.BuildService(_db, alice, "alice")
            .SendMessageAsync(_roomId, alice, "Hello from Alice");
        Assert.NotNull(original);

        ChatMessageDto? reply = await ChatMessageServiceTestSupport.BuildService(_db, bob, "bob")
            .SendMessageAsync(_roomId, bob, "Hi Alice!", original!.MessageId);

        Assert.NotNull(reply);
        Assert.Equal(original.MessageId, reply!.ReplyToMessageId);
        Assert.Equal(alice, reply.ReplyToSenderId);
        Assert.Equal("alice", reply.ReplyToSenderUsername);
        Assert.Equal("Hello from Alice", reply.ReplyToContentSnippet);
    }

    [SkippableFact]
    public async Task Reply_notifies_the_original_sender_via_the_inbox()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        ChatMessageDto? original = await ChatMessageServiceTestSupport.BuildService(_db, alice, "alice")
            .SendMessageAsync(_roomId, alice, "Original message");
        Assert.NotNull(original);

        ChatMessageService bobService = ChatMessageServiceTestSupport.BuildService(_db, bob, "bob");
        ChatMessageDto? reply = await bobService.SendMessageAsync(
            _roomId, bob, "Replying to you", original!.MessageId);
        Assert.NotNull(reply);

        IReadOnlyList<ChatInboxItemDto> aliceInbox = await bobService.GetInboxAsync(alice);

        ChatInboxItemDto notification = Assert.Single(aliceInbox);
        Assert.Equal("Reply", notification.MentionKind);
        Assert.Equal(reply!.MessageId, notification.MessageId);
        Assert.Equal("bob", notification.SenderUsername);
        Assert.Equal("Replying to you", notification.MessageContent);
    }

    [SkippableFact]
    public async Task Replying_to_your_own_message_does_not_self_notify()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(_db, alice, "alice");

        ChatMessageDto? original = await service.SendMessageAsync(_roomId, alice, "First message");
        Assert.NotNull(original);

        ChatMessageDto? reply = await service.SendMessageAsync(
            _roomId, alice, "Replying to myself", original!.MessageId);
        Assert.NotNull(reply);

        IReadOnlyList<ChatInboxItemDto> aliceInbox = await service.GetInboxAsync(alice);
        Assert.Empty(aliceInbox);
    }

    [SkippableFact]
    public async Task Replying_to_a_nonexistent_message_still_sends_as_a_normal_message()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        ChatMessageDto? message = await ChatMessageServiceTestSupport.BuildService(_db, alice, "alice")
            .SendMessageAsync(_roomId, alice, "Stale reply target", Guid.NewGuid());

        Assert.NotNull(message);
        Assert.Null(message!.ReplyToMessageId);
        Assert.Null(message.ReplyToSenderUsername);
    }
}
