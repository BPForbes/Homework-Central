using System.Collections;
using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Security;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Chat;

/// <summary>
/// Real-Postgres coverage for the reply-to-message feature: reply metadata denormalized onto the
/// reply row, and the "reply notifies the original sender" tie-in to the mention/inbox system.
/// Skips gracefully if Postgres isn't reachable, same convention as
/// <c>CustomChannelPrivacyToggleReproTests</c> and <c>ApiIntegrationTests</c>.
/// </summary>
public class ChatMessageServiceReplyTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_CHAT_DATABASE_URL")
        ?? "Host=localhost;Port=5432;Database=homework_central_test_chat;Username=postgres;Password=postgres";

    private bool _databaseAvailable;
    private AppDbContext _db = null!;
    private readonly string _roomId = ChatRoomCatalog.GeneralRoom.Id;

    public async Task InitializeAsync()
    {
        _databaseAvailable = CanConnect();
        if (!_databaseAvailable)
            return;

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        _db = new AppDbContext(options, accessScopeAccessor: null);
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseAvailable)
            await _db.DisposeAsync();
    }

    private bool CanConnect()
    {
        try
        {
            using NpgsqlConnection connection = new(_connectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private ChatMessageService BuildService(Guid userId, string username) =>
        new(
            _db,
            new FakeHttpContextAccessor(BuildHttpContext(userId, username)),
            new AllAccessEffectiveMaskService(),
            new ChatRoomAccessService(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor()),
            new HtmlContentSanitizer(),
            new MentionCooldownTracker(),
            new NoRecipientsMentionResolver(),
            new NoOpHubContext());

    private static HttpContext BuildHttpContext(Guid userId, string username)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("username", username),
            new Claim(TenancyConstants.AccountClassClaimName, AccountClass.RealAccount.ToString()),
        ];
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
        return httpContext;
    }

    [SkippableFact]
    public async Task Reply_denormalizes_parent_sender_and_a_content_snippet()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid alice = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        ChatMessageDto? original = await BuildService(alice, "alice")
            .SendMessageAsync(_roomId, alice, "Hello from Alice");
        Assert.NotNull(original);

        ChatMessageDto? reply = await BuildService(bob, "bob")
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

        ChatMessageDto? original = await BuildService(alice, "alice")
            .SendMessageAsync(_roomId, alice, "Original message");
        Assert.NotNull(original);

        ChatMessageService bobService = BuildService(bob, "bob");
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
        ChatMessageService service = BuildService(alice, "alice");

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
        ChatMessageDto? message = await BuildService(alice, "alice")
            .SendMessageAsync(_roomId, alice, "Stale reply target", Guid.NewGuid());

        Assert.NotNull(message);
        Assert.Null(message!.ReplyToMessageId);
        Assert.Null(message.ReplyToSenderUsername);
    }

    private sealed class FakeHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private sealed class NoRecipientsMentionResolver : IMentionRecipientResolver
    {
        public Task<HashSet<Guid>> ResolveRecipientsAsync(
            string roomId,
            string groupKey,
            IReadOnlyList<ParsedMention> activeMentions,
            Guid senderId,
            AccountClass senderAccountClass,
            string? senderTenantDatabaseName,
            CancellationToken ct = default) =>
            Task.FromResult(new HashSet<Guid>());
    }

    private sealed class AllAccessEffectiveMaskService : IEffectiveMaskService
    {
        public Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserEffectiveMask?>(Build(userId));

        public Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Build(userId));

        public Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default)
        {
            EffectiveMaskDto dto = Build(userId).ToEffectiveMaskDto();
            dto.CustomRoleIds = [];
            return Task.FromResult(dto);
        }

        private static UserEffectiveMask Build(Guid userId)
        {
            BitArray roleMask = BitMask.Create(64);
            BitMask.SetBit(roleMask, PlatformRoles.VerifiedUser);

            BitArray featureMask = BitMask.Create(256);
            BitMask.SetBit(featureMask, PlatformFeatures.PublicMessages);
            BitMask.SetBit(featureMask, PlatformFeatures.GroupMessages);

            return new UserEffectiveMask
            {
                UserId = userId,
                EffectiveRoleMask = roleMask,
                EffectiveModerationMask = BitMask.Create(256),
                EffectiveFeatureMask = featureMask,
                GeneralSubjectMask = BitMask.Create(128),
                StatusMask = BitMask.Create(64),
                UpdatedAt = DateTime.UtcNow,
            };
        }
    }

    private sealed class NoOpHubContext : IHubContext<ChatHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();

        private sealed class NoOpHubClients : IHubClients
        {
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => new NoOpClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class NoOpClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class NoOpGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}
