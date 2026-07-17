using System.Collections;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Uploads;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HomeworkCentral.Api.Tests.Chat;

internal static class ChatMessageServiceTestSupport
{
    internal const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=homework_central_test_chat;Username=postgres;Password=postgres";

    internal static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable("TEST_CHAT_DATABASE_URL") ?? DefaultConnectionString;

    internal static bool CanConnect(string connectionString)
    {
        try
        {
            using NpgsqlConnection connection = new(connectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static async Task<AppDbContext> CreateMigratedDatabaseAsync(string connectionString)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        AppDbContext db = new(options, accessScopeAccessor: null);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    internal static ChatMessageService BuildService(AppDbContext db, Guid userId, string username) =>
        new(
            db,
            new FakeHttpContextAccessor(BuildHttpContext(userId, username)),
            new AllAccessEffectiveMaskService(),
            new ChatRoomAccessService(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor()),
            new MentionCooldownTracker(),
            new NoRecipientsMentionResolver(),
            new NoOpRoleAppearanceService(),
            new NoOpHubContext(),
            new NoOpAssessmentQueue(),
            new NoOpAttachmentAccessTokenService());

    internal static HttpContext BuildHttpContext(Guid userId, string username)
    {
        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("username", username),
            new Claim(TenancyConstants.AccountClassClaimName, AccountClass.RealAccount.ToString()),
        ];
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
    }

    internal sealed class FakeHttpContextAccessor(HttpContext httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    internal sealed class NoRecipientsMentionResolver : IMentionRecipientResolver
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

    internal sealed class NoOpRoleAppearanceService : IRoleAppearanceService
    {
        public Task<string> ResolveSenderColorAsync(BitArray roleMask, CancellationToken ct = default) =>
            Task.FromResult(RoleAppearanceDefaults.FallbackColor);

        public Task<IReadOnlyList<MentionRoleOptionDto>> GetMentionableRolesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MentionRoleOptionDto>>([]);

        public Task<IReadOnlyList<RoleAppearanceDto>> ListRoleAppearanceAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RoleAppearanceDto>>([]);

        public Task<RoleAppearanceDto?> UpdateRoleAppearanceAsync(
            Guid roleId,
            UpdateRoleAppearanceRequest request,
            CancellationToken ct = default) =>
            Task.FromResult<RoleAppearanceDto?>(null);

        public Task<bool> IsMentionablePlatformRoleAsync(string roleName, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<Guid?> TryGetMentionableCustomRoleIdAsync(string roleName, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task PropagateCustomRoleAppearanceAsync(Guid roleId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    internal sealed class AllAccessEffectiveMaskService : IEffectiveMaskService
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

    internal sealed class NoOpHubContext : IHubContext<ChatHub>
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

    internal sealed class NoOpAssessmentQueue : IAssessmentQueue
    {
        public ValueTask EnqueueAsync(AssessmentMessageJob job, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public async IAsyncEnumerable<AssessmentMessageJob> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    internal sealed class NoOpAttachmentAccessTokenService : IAttachmentAccessTokenService
    {
        public string MintDownloadUrl(Guid attachmentId, Guid userId) =>
            $"/api/chat/attachments/{attachmentId}";

        public Task<bool> TryValidateAsync(Guid attachmentId, string accessToken, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
