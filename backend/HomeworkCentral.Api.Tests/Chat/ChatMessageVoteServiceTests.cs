using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeworkCentral.Api.Tests.Chat;

[Collection(nameof(ChatPostgresTestCollection))]
public class ChatMessageVoteServiceTests : IAsyncLifetime
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
    public async Task CastVoteAsync_same_vote_removes_vote_and_zeros_viewer()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid authorId = Guid.NewGuid();
        Guid voterId = Guid.NewGuid();
        ChatMessage message = await SeedMessageAsync(authorId, "author", "Vote target");

        CapturingHubContext hub = new();
        ChatMessageVoteService service = BuildVoteService(_db, hub);

        MessageVoteDto? upvoted = await service.CastVoteAsync(message.MessageId, voterId, 1);
        Assert.NotNull(upvoted);
        Assert.Equal("up", upvoted!.ViewerVote);
        Assert.Equal(1, upvoted.Score);

        MessageVoteDto? removed = await service.CastVoteAsync(message.MessageId, voterId, 1);
        Assert.NotNull(removed);
        Assert.Null(removed!.ViewerVote);
        Assert.Equal(0, removed.Score);
        Assert.Equal(0, removed.UpvoteCount);
        Assert.Equal(0, removed.DownvoteCount);

        bool voteExists = await _db.ChatMessageVotes.AnyAsync(
            v => v.MessageId == message.MessageId && v.UserId == voterId);
        Assert.False(voteExists);
    }

    [SkippableFact]
    public async Task CastVoteAsync_switch_vote_updates_value()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid authorId = Guid.NewGuid();
        Guid voterId = Guid.NewGuid();
        ChatMessage message = await SeedMessageAsync(authorId, "author", "Switch vote target");

        ChatMessageVoteService service = BuildVoteService(_db, new CapturingHubContext());

        await service.CastVoteAsync(message.MessageId, voterId, 1);
        MessageVoteDto? switched = await service.CastVoteAsync(message.MessageId, voterId, -1);

        Assert.NotNull(switched);
        Assert.Equal("down", switched!.ViewerVote);
        Assert.Equal(-1, switched.Score);
        Assert.Equal(0, switched.UpvoteCount);
        Assert.Equal(1, switched.DownvoteCount);
    }

    [SkippableFact]
    public async Task CastVoteAsync_remove_broadcasts_MessageVoteUpdated()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid authorId = Guid.NewGuid();
        Guid voterId = Guid.NewGuid();
        ChatMessage message = await SeedMessageAsync(authorId, "author", "Broadcast target");

        CapturingHubContext hub = new();
        ChatMessageVoteService service = BuildVoteService(_db, hub);

        await service.CastVoteAsync(message.MessageId, voterId, -1);
        hub.Sent.Clear();

        MessageVoteDto? removed = await service.CastVoteAsync(message.MessageId, voterId, -1);
        Assert.NotNull(removed);

        CapturedHubMessage broadcast = Assert.Single(hub.Sent);
        Assert.Equal("MessageVoteUpdated", broadcast.Method);
        MessageVoteDto payload = Assert.IsType<MessageVoteDto>(broadcast.Payload);
        Assert.Null(payload.ViewerVote);
        Assert.Equal(message.MessageId, payload.MessageId);
    }

    private async Task<ChatMessage> SeedMessageAsync(Guid senderId, string username, string content)
    {
        ChatMessageService senderService = ChatMessageServiceTestSupport.BuildService(_db, senderId, username);
        ChatMessageDto? dto = await senderService.SendMessageAsync(_roomId, senderId, content);
        Assert.NotNull(dto);

        ChatMessage? message = await _db.ChatMessages.FirstOrDefaultAsync(m => m.MessageId == dto!.MessageId);
        Assert.NotNull(message);
        return message!;
    }

    private static ChatMessageVoteService BuildVoteService(AppDbContext db, IHubContext<ChatHub> hub) =>
        new(
            db,
            new ChatMessageServiceTestSupport.AllAccessEffectiveMaskService(),
            new ChatRoomAccessService(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor()),
            hub);

    private sealed class CapturingHubContext : IHubContext<ChatHub>
    {
        public List<CapturedHubMessage> Sent { get; } = [];

        public IHubClients Clients => new CapturingHubClients(this);

        public IGroupManager Groups { get; } = new NoOpGroupManager();

        private sealed class NoOpGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class CapturingHubClients(CapturingHubContext owner) : IHubClients
        {
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => new CapturingClientProxy(owner);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class CapturingClientProxy(CapturingHubContext owner) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.Sent.Add(new CapturedHubMessage(method, args?.FirstOrDefault()));
                return Task.CompletedTask;
            }
        }
    }

    private sealed record CapturedHubMessage(string Method, object? Payload);
}
