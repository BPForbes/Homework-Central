using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeworkCentral.Api.Tests.Chat;

[Collection(nameof(ChatPostgresTestCollection))]
public class ChatInboxDeleteTests : IAsyncLifetime
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
    public async Task DeleteInboxItems_removes_only_the_selected_notifications_for_that_user()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();
        Guid carol = Guid.NewGuid();

        ChatMessageService aliceService = ChatMessageServiceTestSupport.BuildService(_db, alice, "alice");
        ChatMessageService bobService = ChatMessageServiceTestSupport.BuildService(_db, bob, "bob");

        ChatMessageDto? first = await aliceService.SendMessageAsync(_roomId, alice, "First");
        ChatMessageDto? second = await aliceService.SendMessageAsync(_roomId, alice, "Second");
        Assert.NotNull(first);
        Assert.NotNull(second);

        await bobService.SendMessageAsync(_roomId, bob, "Reply one", first!.MessageId);
        await bobService.SendMessageAsync(_roomId, bob, "Reply two", second!.MessageId);

        IReadOnlyList<ChatInboxItemDto> aliceInbox = await bobService.GetInboxAsync(alice);
        Assert.Equal(2, aliceInbox.Count);

        Guid toDelete = aliceInbox[0].NotificationId;
        int deleted = await bobService.DeleteInboxItemsAsync(alice, [toDelete]);
        Assert.Equal(1, deleted);

        IReadOnlyList<ChatInboxItemDto> remaining = await bobService.GetInboxAsync(alice);
        Assert.Single(remaining);
        Assert.NotEqual(toDelete, remaining[0].NotificationId);

        IReadOnlyList<ChatInboxItemDto> carolInbox = await bobService.GetInboxAsync(carol);
        Assert.Empty(carolInbox);
    }

    [SkippableFact]
    public async Task DeleteInboxAll_removes_every_notification_for_the_user()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        ChatMessageService aliceService = ChatMessageServiceTestSupport.BuildService(_db, alice, "alice");
        ChatMessageService bobService = ChatMessageServiceTestSupport.BuildService(_db, bob, "bob");

        ChatMessageDto? original = await aliceService.SendMessageAsync(_roomId, alice, "Ping");
        Assert.NotNull(original);

        await bobService.SendMessageAsync(_roomId, bob, "Pong", original!.MessageId);
        Assert.Single(await bobService.GetInboxAsync(alice));

        int deleted = await bobService.DeleteInboxAllAsync(alice);
        Assert.Equal(1, deleted);
        Assert.Empty(await bobService.GetInboxAsync(alice));
    }

    [SkippableFact]
    public async Task DeleteInboxCategory_removes_only_notifications_from_accessible_category()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid recipient = Guid.NewGuid();
        ChatRoomDefinition scienceRoom = ChatRoomCatalog.SubjectRooms
            .First(room => room.CategoryKey == SubjectMaskNames.Science);

        _db.ChatMentionNotifications.AddRange(
            CreateNotification(recipient, ChatRoomCatalog.GeneralRoom),
            CreateNotification(recipient, scienceRoom));
        await _db.SaveChangesAsync();

        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(
            _db,
            recipient,
            "recipient");

        int inaccessibleDeleted = await service.DeleteInboxCategoryAsync(
            recipient,
            SubjectMaskNames.Science);
        Assert.Equal(0, inaccessibleDeleted);

        int deleted = await service.DeleteInboxCategoryAsync(
            recipient,
            ChatRoomBlueprint.GeneralCategoryKey);
        Assert.Equal(1, deleted);

        List<ChatMentionNotification> remaining = await _db.ChatMentionNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == recipient)
            .ToListAsync();
        ChatMentionNotification notification = Assert.Single(remaining);
        Assert.Equal(scienceRoom.Id, notification.RoomId);
    }

    [SkippableFact]
    public async Task DeleteInboxItems_does_not_remove_another_users_notifications()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        ChatMessageService aliceService = ChatMessageServiceTestSupport.BuildService(_db, alice, "alice");
        ChatMessageService bobService = ChatMessageServiceTestSupport.BuildService(_db, bob, "bob");

        ChatMessageDto? original = await aliceService.SendMessageAsync(_roomId, alice, "Hello");
        Assert.NotNull(original);

        await bobService.SendMessageAsync(_roomId, bob, "Hi", original!.MessageId);

        ChatInboxItemDto bobNotification = Assert.Single(await bobService.GetInboxAsync(alice));
        int deleted = await bobService.DeleteInboxItemsAsync(bob, [bobNotification.NotificationId]);
        Assert.Equal(0, deleted);
        Assert.Single(await bobService.GetInboxAsync(alice));
    }

    [SkippableFact]
    public async Task GetInbox_filters_collective_and_category_views_to_accessible_rooms()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid recipient = Guid.NewGuid();
        ChatRoomDefinition scienceRoom = ChatRoomCatalog.SubjectRooms
            .First(room => room.CategoryKey == SubjectMaskNames.Science);

        _db.ChatMentionNotifications.AddRange(
            CreateNotification(recipient, ChatRoomCatalog.GeneralRoom),
            CreateNotification(recipient, scienceRoom));
        await _db.SaveChangesAsync();

        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(
            _db,
            recipient,
            "recipient");

        ChatInboxItemDto collectiveItem = Assert.Single(await service.GetInboxAsync(recipient));
        Assert.Equal(ChatRoomCatalog.GeneralRoom.Id, collectiveItem.RoomId);

        ChatInboxItemDto generalItem = Assert.Single(
            await service.GetInboxAsync(recipient, ChatRoomBlueprint.GeneralCategoryKey));
        Assert.Equal(ChatRoomCatalog.GeneralRoom.Id, generalItem.RoomId);

        Assert.Empty(await service.GetInboxAsync(recipient, SubjectMaskNames.Science));

        ChatInboxSummaryDto summary = await service.GetInboxSummaryAsync(recipient);
        ChatInboxSummaryItemDto generalSummary = Assert.Single(summary.Categories);
        Assert.Equal(ChatRoomBlueprint.GeneralCategoryKey, generalSummary.CategoryKey);
        Assert.Equal(1, generalSummary.UnreadCount);
    }

    [SkippableFact]
    public async Task GetInboxSummary_includes_accessible_categories_with_zero_unread_items()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid recipient = Guid.NewGuid();
        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(
            _db,
            recipient,
            "recipient");

        ChatInboxSummaryDto summary = await service.GetInboxSummaryAsync(recipient);

        ChatInboxSummaryItemDto generalSummary = Assert.Single(summary.Categories);
        Assert.Equal(ChatRoomBlueprint.GeneralCategoryKey, generalSummary.CategoryKey);
        Assert.Equal(0, generalSummary.UnreadCount);
    }

    private static ChatMentionNotification CreateNotification(
        Guid recipientUserId,
        ChatRoomDefinition room) =>
        new()
        {
            NotificationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            SenderId = Guid.NewGuid(),
            SenderUsername = "sender",
            RoomId = room.Id,
            RoomDisplayName = room.RoomDisplayName,
            CategoryKey = room.CategoryKey,
            CategoryDisplayName = room.CategoryDisplayName,
            MessageContent = "Mention",
            MentionKind = "Mention",
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = AccountClass.RealAccount,
        };
}
