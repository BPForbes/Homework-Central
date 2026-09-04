using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Tickets;

/// <summary>
/// Ensures the two default ticket portals exist (Tutor applications + Notify Mods) under General
/// on the given database for the given <see cref="AccountClass"/>. Callers must seed against the
/// master DB for both <see cref="AccountClass.RealAccount"/> and
/// <see cref="AccountClass.DeveloperAccount"/> — <see cref="Infrastructure.ICustomChannelStore"/>
/// and ticket APIs do not load portals from persona tenant databases.
/// Idempotent: creates missing portals and reconciles category/description on existing ones.
/// </summary>
public static class TicketPortalSeedData
{
    public const string TutorPortalDescription =
        "Do you wish to become a tutor? Fill out a ticket to begin your application. "
        + "Please note this is a free service. This ticket stream should not be used to get tutoring aid directly.";

    public const string ModPortalDescription =
        "Do you see any users breaking rules? Contact moderators here!";

    public static async Task SeedAsync(
        AppDbContext db,
        AccountClass ownerAccountClass,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await EnsurePortalAsync(db, ownerAccountClass, CreateTutorPortalDefinition(), logger, ct);
        await EnsurePortalAsync(db, ownerAccountClass, CreateModPortalDefinition(), logger, ct);

        await db.SaveChangesAsync(ct);
    }

    private static TicketPortalSeedDefinition CreateTutorPortalDefinition() =>
        new()
        {
            DisplayName = DefaultTicketPortalPresets.TutorDisplayName,
            FilterName = DefaultTicketPortalPresets.TutorFilterName,
            TrackingMode = TicketTrackingModes.Opener,
            DecisionLabels = DefaultTicketPortalPresets.TutorDecisionLabels,
            MentionRules = DefaultTicketPortalPresets.TutorMentionRules(),
            StaffRules = DefaultTicketPortalPresets.TutorStaffRules(),
            Intake = DefaultTicketPortalPresets.TutorIntakeQuestions(),
            CtaLabel = "Apply",
            Description = TutorPortalDescription,
            TrackingInstructions =
                "Monitor subject-channel responses related to applied subjects. Score direct subjects strictly, "
                + "related subjects mildly, unrelated subjects reward-only.",
        };

    private static TicketPortalSeedDefinition CreateModPortalDefinition() =>
        new()
        {
            DisplayName = DefaultTicketPortalPresets.ModDisplayName,
            FilterName = DefaultTicketPortalPresets.ModFilterName,
            TrackingMode = TicketTrackingModes.FromIntakeField,
            DecisionLabels = DefaultTicketPortalPresets.ModDecisionLabels,
            MentionRules = DefaultTicketPortalPresets.ModMentionRules(),
            StaffRules = DefaultTicketPortalPresets.ModStaffRules(),
            Intake = DefaultTicketPortalPresets.ModIntakeQuestions(),
            CtaLabel = "Notify Mods",
            Description = ModPortalDescription,
            TrackingInstructions =
                "Cross-examine reported reason against proof and subsequent messages from the reported user(s).",
        };

    private static async Task EnsurePortalAsync(
        AppDbContext db,
        AccountClass ownerAccountClass,
        TicketPortalSeedDefinition definition,
        ILogger? logger,
        CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        TicketPortalConfig? existing = await FindExistingPortalAsync(db, ownerAccountClass, definition, ct);

        if (existing is not null)
        {
            if (ReconcileExistingPortal(existing, definition, now))
                logger?.LogInformation(
                    "Updated default ticket portal '{DisplayName}' (filter {FilterName}) for {AccountClass}.",
                    definition.DisplayName,
                    definition.FilterName,
                    ownerAccountClass);

            return;
        }

        CreatePortal(db, ownerAccountClass, definition, now);
        logger?.LogInformation(
            "Seeded default ticket portal '{DisplayName}' (filter {FilterName}) for {AccountClass}.",
            definition.DisplayName,
            definition.FilterName,
            ownerAccountClass);
    }

    private static Task<TicketPortalConfig?> FindExistingPortalAsync(
        AppDbContext db,
        AccountClass ownerAccountClass,
        TicketPortalSeedDefinition definition,
        CancellationToken ct) =>
        db.TicketPortalConfigs
            .IgnoreQueryFilters()
            .Include(portal => portal.Channel)
            .FirstOrDefaultAsync(
                portal => !portal.Channel.IsArchived
                          && portal.Channel.OwnerAccountClass == ownerAccountClass
                          && (portal.FilterName == definition.FilterName
                              || portal.Purpose == definition.FilterName
                              || portal.Channel.DisplayName == definition.DisplayName),
                ct);

    private static bool ReconcileExistingPortal(
        TicketPortalConfig existing,
        TicketPortalSeedDefinition definition,
        DateTime now)
    {
        // Both reconcilers mutate; evaluate each before combining so a channel
        // text update cannot skip portal field reconciliation (or the reverse).
        bool channelChanged = ReconcileChannel(existing.Channel);
        bool portalChanged = ReconcilePortalText(existing, definition);
        bool changed = channelChanged || portalChanged;

        if (changed)
        {
            existing.Channel.UpdatedAtUtc = now;
            existing.UpdatedAtUtc = now;
        }

        return changed;
    }

    private static bool ReconcileChannel(CustomChannel channel)
    {
        bool changed = false;
        if (!string.Equals(channel.CategoryKey, ChatRoomBlueprint.GeneralCategoryKey, StringComparison.Ordinal))
        {
            channel.CategoryKey = ChatRoomBlueprint.GeneralCategoryKey;
            changed = true;
        }

        if (!string.Equals(
                channel.CategoryDisplayName,
                ChatRoomBlueprint.GeneralCategoryDisplayName,
                StringComparison.Ordinal))
        {
            channel.CategoryDisplayName = ChatRoomBlueprint.GeneralCategoryDisplayName;
            changed = true;
        }

        if (channel.IsPrivate)
        {
            channel.IsPrivate = false;
            changed = true;
        }

        return changed;
    }

    private static bool ReconcilePortalText(
        TicketPortalConfig existing,
        TicketPortalSeedDefinition definition)
    {
        bool changed = false;
        if (!string.Equals(existing.Description, definition.Description, StringComparison.Ordinal))
        {
            existing.Description = definition.Description;
            changed = true;
        }

        if (!string.Equals(existing.CtaLabel, definition.CtaLabel, StringComparison.Ordinal))
        {
            existing.CtaLabel = definition.CtaLabel;
            changed = true;
        }

        return changed;
    }

    private static void CreatePortal(
        AppDbContext db,
        AccountClass ownerAccountClass,
        TicketPortalSeedDefinition definition,
        DateTime now)
    {
        Guid channelId = Guid.NewGuid();
        string roomId = CustomChannelIds.BuildRoomId(channelId);

        db.CustomChannels.Add(new CustomChannel
        {
            ChannelId = channelId,
            RoomId = roomId,
            DisplayName = definition.DisplayName,
            IconName = "ticket",
            CategoryKey = ChatRoomBlueprint.GeneralCategoryKey,
            CategoryDisplayName = ChatRoomBlueprint.GeneralCategoryDisplayName,
            RoomType = CustomRoomType.Ticket,
            IsPrivate = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = Guid.Empty,
            OwnerAccountClass = ownerAccountClass,
            TieType = ChannelTieType.None,
        });

        db.TicketPortalConfigs.Add(new TicketPortalConfig
        {
            ChannelId = channelId,
            CtaLabel = definition.CtaLabel,
            Description = definition.Description,
            Purpose = definition.DisplayName,
            FilterName = definition.FilterName,
            NextDisplayNumber = 1,
            TrackingMode = definition.TrackingMode,
            TrackingInstructions = definition.TrackingInstructions,
            DecisionLabelsJson = TicketJson.SerializeStringList(definition.DecisionLabels),
            MentionRoleRulesJson = TicketJson.SerializeAccessRules(definition.MentionRules),
            StaffAccessRulesJson = TicketJson.SerializeAccessRules(definition.StaffRules),
            IntakeSchemaJson = TicketJson.SerializeIntakeSchema(definition.Intake),
            UpdatedAtUtc = now,
        });
    }

    private sealed class TicketPortalSeedDefinition
    {
        public string DisplayName { get; init; } = string.Empty;
        public string FilterName { get; init; } = string.Empty;
        public string TrackingMode { get; init; } = string.Empty;
        public IReadOnlyList<string> DecisionLabels { get; init; } = [];
        public IReadOnlyList<CustomChannelAccessRuleInput> MentionRules { get; init; } = [];
        public IReadOnlyList<CustomChannelAccessRuleInput> StaffRules { get; init; } = [];
        public IReadOnlyList<TicketIntakeQuestionDto> Intake { get; init; } = [];
        public string CtaLabel { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string TrackingInstructions { get; init; } = string.Empty;
    }
}
