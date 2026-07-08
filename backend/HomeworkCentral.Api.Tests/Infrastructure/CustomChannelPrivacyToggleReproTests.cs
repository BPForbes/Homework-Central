using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tests.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace HomeworkCentral.Api.Tests.Infrastructure;

/// <summary>
/// Regression coverage for a real-Postgres bug: toggling a custom room private (adding new
/// <see cref="CustomChannelAccessRule"/> rows) threw <see cref="DbUpdateConcurrencyException"/>
/// because the rows were appended only to an already-tracked channel's navigation collection.
/// EF's automatic change detection then used the "is the key already set?" heuristic to decide
/// Added vs. Modified for the new rule — since <c>AccessRuleId</c> has a store-generated default
/// (<c>gen_random_uuid()</c>) and the code assigns a non-empty <see cref="Guid"/> up front, EF
/// concluded the row already existed and issued an UPDATE instead of an INSERT, which naturally
/// affects 0 rows. The EF Core in-memory provider doesn't reproduce this (it has no server-side
/// key generation semantics), so this suite talks to a real Postgres instance and skips
/// gracefully when one isn't reachable — same convention as <c>ApiIntegrationTests</c>.
/// </summary>
public class CustomChannelPrivacyToggleReproTests : IAsyncLifetime
{
    // Uses a database name distinct from ApiIntegrationTests' default (homework_central_test) so
    // the two suites never race to migrate/reset the same database when run together without an
    // explicit TEST_DATABASE_URL override.
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_INFRA_DATABASE_URL")
        ?? "Host=localhost;Port=5432;Database=homework_central_test_infra;Username=postgres;Password=postgres";

    private bool _databaseAvailable;
    private AppDbContext _db = null!;
    private Guid _actorUserId;
    private Guid _customRoleId;

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

        User user = new()
        {
            UserId = Guid.NewGuid(),
            Email = "repro@example.com",
            Username = "reprouser",
            PasswordHash = "x",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        _actorUserId = user.UserId;

        Role customRole = new()
        {
            RoleId = Guid.NewGuid(),
            Name = "ReproCustomRole",
            IsCustom = true,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = AccountClass.RealAccount,
        };
        _db.Roles.Add(customRole);
        _customRoleId = customRole.RoleId;

        await _db.SaveChangesAsync();
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

    private InfrastructureService BuildService()
    {
        ConfigurationBuilder configBuilder = new();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MasterConnection"] = _connectionString,
        });
        IConfiguration config = configBuilder.Build();

        TenantConnectionResolver connectionResolver = new(config);
        MasterDbContext masterRegistry = new(
            new DbContextOptionsBuilder<MasterDbContext>().UseNpgsql(_connectionString).Options);
        TenantDbContextFactory tenantFactory = new(connectionResolver, masterRegistry);
        FixedAccessScopeAccessor scopeAccessor = new(AccountClass.RealAccount);

        InfrastructureUserDirectory userDirectory = new(_db, masterRegistry, tenantFactory, scopeAccessor);

        return new InfrastructureService(
            _db,
            new RoleMaskService(_db),
            new AllPermissionsEffectiveMaskService(),
            new NoOpCustomChannelStore(),
            new AlwaysConfirmPasswordConfirmationService(),
            new ChatRoomAccessService(new NoOpCustomChannelStore(), scopeAccessor),
            scopeAccessor,
            new NoOpChatNavNotifier(),
            userDirectory,
            masterRegistry,
            tenantFactory);
    }

    [SkippableFact]
    public async Task Toggling_privacy_back_and_forth_repeatedly_does_not_throw()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_DATABASE_URL.");

        InfrastructureService service = BuildService();

        CustomChannelDto created = await service.CreateCustomChannelAsync(_actorUserId, new CreateCustomChannelRequest
        {
            DisplayName = "Repro Room",
            CategoryKey = "Custom",
            CategoryDisplayName = "Custom",
            RoomType = "Chat",
            IsPrivate = false,
            AccessRules = [],
        });

        for (int i = 0; i < 6; i++)
        {
            bool goPrivate = i % 2 == 0;

            UpdateCustomChannelRequest request = goPrivate
                ? new UpdateCustomChannelRequest
                {
                    DisplayName = "Repro Room",
                    IsPrivate = true,
                    AccessRules = [new CustomChannelAccessRuleInput { CustomRoleId = _customRoleId }],
                }
                : new UpdateCustomChannelRequest
                {
                    DisplayName = "Repro Room",
                    IsPrivate = false,
                };

            CustomChannelDto? updated = await service.UpdateCustomChannelAsync(_actorUserId, created.ChannelId, request);

            Assert.NotNull(updated);
            Assert.Equal(goPrivate, updated!.IsPrivate);
            Assert.Equal(goPrivate ? 1 : 0, updated.AccessRules.Count);
        }
    }

    [SkippableFact]
    public async Task Creating_a_private_room_with_access_rules_does_not_throw()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_DATABASE_URL.");

        InfrastructureService service = BuildService();

        CustomChannelDto created = await service.CreateCustomChannelAsync(_actorUserId, new CreateCustomChannelRequest
        {
            DisplayName = "Private From Birth",
            CategoryKey = "Custom",
            CategoryDisplayName = "Custom",
            RoomType = "Chat",
            IsPrivate = true,
            AccessRules = [new CustomChannelAccessRuleInput { CustomRoleId = _customRoleId }],
        });

        Assert.True(created.IsPrivate);
        Assert.Single(created.AccessRules);
    }

    [SkippableFact]
    public async Task Swapping_access_rules_on_an_already_private_room_does_not_throw()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_DATABASE_URL.");

        InfrastructureService service = BuildService();

        CustomChannelDto created = await service.CreateCustomChannelAsync(_actorUserId, new CreateCustomChannelRequest
        {
            DisplayName = "Repro Room 2",
            CategoryKey = "Custom",
            CategoryDisplayName = "Custom",
            RoomType = "Chat",
            IsPrivate = true,
            AccessRules = [new CustomChannelAccessRuleInput { CustomRoleId = _customRoleId }],
        });

        CustomChannelDto? updated = await service.UpdateCustomChannelAsync(_actorUserId, created.ChannelId, new UpdateCustomChannelRequest
        {
            IsPrivate = true,
            AccessRules = [new CustomChannelAccessRuleInput { PlatformRoleBit = PlatformRoles.Administrator }],
        });

        Assert.NotNull(updated);
        Assert.True(updated!.IsPrivate);
        Assert.Single(updated.AccessRules);
        Assert.Equal(PlatformRoles.Administrator, updated.AccessRules[0].PlatformRoleBit);
    }
}

internal sealed class NoOpCustomChannelStore : ICustomChannelStore
{
    public IReadOnlyList<CustomChannelSnapshot> Channels => [];
    public CustomChannelSnapshot? FindByRoomId(string roomId) => null;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NoOpChatNavNotifier : IChatNavNotifier
{
    public Task NotifyNavChangedAsync(AccountClass accountClass, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class AlwaysConfirmPasswordConfirmationService : IPasswordConfirmationService
{
    public Task<bool> VerifyAsync(Guid userId, string password, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>Grants every platform role bit (including Owner) so infra edit-window/permission checks always pass.</summary>
internal sealed class AllPermissionsEffectiveMaskService : IEffectiveMaskService
{
    public Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<UserEffectiveMask?>(Build(userId));

    public Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Build(userId));

    public Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(new EffectiveMaskDto());

    private static UserEffectiveMask Build(Guid userId) => new()
    {
        UserId = userId,
        EffectiveRoleMask = new System.Collections.BitArray(64, true),
        EffectiveModerationMask = new System.Collections.BitArray(256, true),
        EffectiveFeatureMask = new System.Collections.BitArray(256, true),
        GeneralSubjectMask = new System.Collections.BitArray(128),
        StatusMask = new System.Collections.BitArray(64),
        UpdatedAt = DateTime.UtcNow,
    };
}
