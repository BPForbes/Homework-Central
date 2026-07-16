using System.Collections;
using System.Runtime.CompilerServices;
using HomeworkCentral.Api.Assessment;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeworkCentral.Api.Tests.Chat;

public class ChatMessageServiceTests
{
    [Fact]
    public async Task Verified_user_with_general_subject_claim_can_access_subject_expertise_room_without_group_messages_feature()
    {
        Guid userId = Guid.NewGuid();
        ChatRoomDefinition csharpRoom = ChatRoomCatalog.SubjectRooms.Single(room =>
            room.ExpertiseCategory == SubjectMaskNames.ComputerScience
            && room.RoomDisplayName == "C#");

        EffectiveMaskDto masks = CreateMasks(
            roles: [PlatformRoles.VerifiedUser],
            generalSubjects: [GeneralSubjects.ComputerScience],
            includeGroupMessages: false);

        ChatMessageService service = CreateService(userId, masks);

        Assert.True(await service.CanAccessRoomAsync(csharpRoom.Id, userId));
    }

    [Fact]
    public async Task Verified_user_without_group_messages_still_cannot_access_staff_rooms()
    {
        Guid userId = Guid.NewGuid();
        ChatRoomDefinition tutorsRoom = ChatRoomCatalog.StaffRooms.Single(room => room.RoomDisplayName == "Tutors");

        EffectiveMaskDto masks = CreateMasks(
            roles: [PlatformRoles.VerifiedUser],
            includeGroupMessages: false);

        ChatMessageService service = CreateService(userId, masks);

        Assert.False(await service.CanAccessRoomAsync(tutorsRoom.Id, userId));
    }

    private static ChatMessageService CreateService(Guid userId, EffectiveMaskDto masks)
    {
        UserEffectiveMask effectiveMask = ToUserEffectiveMask(userId, masks);
        FakeEffectiveMaskService effectiveMaskService = new(effectiveMask);

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>().Options;
        AppDbContext db = new(options);

        return new ChatMessageService(
            db,
            new FakeHttpContextAccessor(),
            effectiveMaskService,
            new ChatRoomAccessService(new EmptyCustomChannelStore(), new FixedAccessScopeAccessor()),
            new MentionCooldownTracker(),
            new FakeMentionRecipientResolver(),
            new FakeRoleAppearanceService(),
            new FakeHubContext(),
            new FakeAssessmentQueue());
    }

    private static UserEffectiveMask ToUserEffectiveMask(Guid userId, EffectiveMaskDto dto) =>
        new()
        {
            UserId = userId,
            EffectiveRoleMask = BitMask.FromBase64(dto.RoleMask, 64),
            EffectiveModerationMask = BitMask.FromBase64(dto.ModerationMask, 256),
            EffectiveFeatureMask = BitMask.FromBase64(dto.FeatureMask, 256),
            GeneralSubjectMask = BitMask.FromBase64(dto.GeneralSubjectMask, 128),
            StatusMask = BitMask.FromBase64(dto.StatusMask, 64),
            SubjectExpertiseMasks = dto.SubjectExpertiseMasks
                .Select(pair => new UserSubjectExpertiseMask
                {
                    UserId = userId,
                    Category = pair.Key,
                    ExpertiseMask = BitMask.FromBase64(pair.Value, 128),
                })
                .ToList(),
        };

    private static EffectiveMaskDto CreateMasks(
        IEnumerable<short>? roles = null,
        IEnumerable<short>? generalSubjects = null,
        bool includeGroupMessages = true)
    {
        BitArray roleMask = BitMask.Create(64);
        foreach (short bit in roles ?? [])
            BitMask.SetBit(roleMask, bit);

        BitArray featureMask = BitMask.Create(256);
        BitMask.SetBit(featureMask, PlatformFeatures.PublicMessages);
        if (includeGroupMessages)
            BitMask.SetBit(featureMask, PlatformFeatures.GroupMessages);

        BitArray generalSubjectMask = BitMask.Create(128);
        foreach (short bit in generalSubjects ?? [])
            BitMask.SetBit(generalSubjectMask, bit);

        Dictionary<string, string> expertiseMasks = SubjectExpertiseCatalog.AllExpertiseCategoryNames()
            .ToDictionary(category => category, _ => BitMask.ToBase64(BitMask.Create(128)), StringComparer.Ordinal);

        return new EffectiveMaskDto
        {
            RoleMask = BitMask.ToBase64(roleMask),
            ModerationMask = BitMask.ToBase64(BitMask.Create(256)),
            FeatureMask = BitMask.ToBase64(featureMask),
            GeneralSubjectMask = BitMask.ToBase64(generalSubjectMask),
            SubjectExpertiseMasks = expertiseMasks,
            StatusMask = BitMask.ToBase64(BitMask.Create(64)),
        };
    }

    private sealed class FakeMentionRecipientResolver : IMentionRecipientResolver
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

    private sealed class FakeRoleAppearanceService : IRoleAppearanceService
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

    private sealed class FakeEffectiveMaskService(UserEffectiveMask mask) : IEffectiveMaskService
    {
        public Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserEffectiveMask?>(mask);

        public Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(mask);

        public Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default)
        {
            EffectiveMaskDto dto = mask.ToEffectiveMaskDto();
            dto.CustomRoleIds = [];
            return Task.FromResult(dto);
        }
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class FakeHubContext : IHubContext<ChatHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups { get; } = new FakeGroupManager();

        private sealed class FakeHubClients : IHubClients
        {
            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => new FakeClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class FakeClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class FakeGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }

    private sealed class FakeAssessmentQueue : IAssessmentQueue
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
}
