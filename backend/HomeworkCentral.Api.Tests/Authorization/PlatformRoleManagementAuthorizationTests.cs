using System.Collections;
using System.Security.Claims;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tests.Chat;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Authorization;

namespace HomeworkCentral.Api.Tests.Authorization;

public class PlatformRoleManagementAuthorizationTests
{
    [Fact]
    public async Task DevAdmin_account_class_succeeds_without_bitmask()
    {
        FixedEffectiveMaskService maskService = new(null);
        PlatformRoleManagementAuthorizationHandler handler = new(
            maskService,
            new FixedAccessScopeAccessor(AccountClass.DevAdmin));
        AuthorizationHandlerContext context = CreateContext(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "test")));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal(0, maskService.GetCalls);
    }

    [Fact]
    public async Task ManageRoles_bit_succeeds_for_platform_role_management()
    {
        Guid userId = Guid.NewGuid();
        BitArray moderation = BitMask.Create(256);
        BitMask.SetBit(moderation, ModerationPermissions.ManageRoles);

        FixedEffectiveMaskService maskService = new(new UserEffectiveMask
        {
            UserId = userId,
            EffectiveModerationMask = moderation,
        });
        PlatformRoleManagementAuthorizationHandler handler = new(
            maskService,
            new FixedAccessScopeAccessor(AccountClass.RealAccount));
        AuthorizationHandlerContext context = CreateContext(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "test")));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(ClaimsPrincipal user) =>
        new(
            [new PlatformRoleManagementRequirement()],
            user,
            resource: null);

    private sealed class FixedEffectiveMaskService(UserEffectiveMask? mask) : IEffectiveMaskService
    {
        public int GetCalls { get; private set; }

        public Task<UserEffectiveMask?> GetUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default)
        {
            GetCalls++;
            return Task.FromResult(mask);
        }

        public Task<UserEffectiveMask> RebuildUserEffectiveMaskAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(mask ?? throw new InvalidOperationException("Missing mask."));

        public Task<EffectiveMaskDto> GetEffectiveMaskDtoAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new EffectiveMaskDto());
    }
}
