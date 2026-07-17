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
/// Ensures the two default ticket portals exist (Tutor applications + Notify Mods) under General.
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
        await EnsurePortalAsync(
            db,
            ownerAccountClass,
            displayName: DefaultTicketPortalPresets.TutorDisplayName,
            filterName: DefaultTicketPortalPresets.TutorFilterName,
            trackingMode: TicketTrackingModes.Opener,
            decisionLabels: DefaultTicketPortalPresets.TutorDecisionLabels,
            mentionRules: DefaultTicketPortalPresets.TutorMentionRules(),
            staffRules: DefaultTicketPortalPresets.TutorStaffRules(),
            intake: DefaultTicketPortalPresets.TutorIntakeQuestions(),
            ctaLabel: "Apply",
            description: TutorPortalDescription,
            trackingInstructions:
                "Monitor subject-channel responses related to applied subjects. Score direct subjects strictly, "
                + "related subjects mildly, unrelated subjects reward-only.",
            logger,
            ct);

        await EnsurePortalAsync(
            db,
            ownerAccountClass,
            displayName: DefaultTicketPortalPresets.ModDisplayName,
            filterName: DefaultTicketPortalPresets.ModFilterName,
            trackingMode: TicketTrackingModes.FromIntakeField,
            decisionLabels: DefaultTicketPortalPresets.ModDecisionLabels,
            mentionRules: DefaultTicketPortalPresets.ModMentionRules(),
            staffRules: DefaultTicketPortalPresets.ModStaffRules(),
            intake: DefaultTicketPortalPresets.ModIntakeQuestions(),
            ctaLabel: "Notify Mods",
            description: ModPortalDescription,
            trackingInstructions:
                "Cross-examine reported reason against proof and subsequent messages from the reported user(s).",
            logger,
            ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsurePortalAsync(
        AppDbContext db,
        AccountClass ownerAccountClass,
        string displayName,
        string filterName,
        string trackingMode,
        IReadOnlyList<string> decisionLabels,
        IReadOnlyList<CustomChannelAccessRuleInput> mentionRules,
        IReadOnlyList<CustomChannelAccessRuleInput> staffRules,
        IReadOnlyList<TicketIntakeQuestionDto> intake,
        string ctaLabel,
        string description,
        string trackingInstructions,
        ILogger? logger,
        CancellationToken ct)
    {
        TicketPortalConfig? existing = await db.TicketPortalConfigs
            .IgnoreQueryFilters()
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(
                p => !p.Channel.IsArchived
                     && p.Channel.OwnerAccountClass == ownerAccountClass
                     && (p.FilterName == filterName
                         || p.Purpose == filterName
                         || p.Channel.DisplayName == displayName),
                ct);

        DateTime now = DateTime.UtcNow;

        if (existing is not null)
        {
            bool changed = false;
            if (!string.Equals(existing.Channel.CategoryKey, ChatRoomBlueprint.GeneralCategoryKey, StringComparison.Ordinal))
            {
                existing.Channel.CategoryKey = ChatRoomBlueprint.GeneralCategoryKey;
                changed = true;
            }

            if (!string.Equals(
                    existing.Channel.CategoryDisplayName,
                    ChatRoomBlueprint.GeneralCategoryDisplayName,
                    StringComparison.Ordinal))
            {
                existing.Channel.CategoryDisplayName = ChatRoomBlueprint.GeneralCategoryDisplayName;
                changed = true;
            }

            if (existing.Channel.IsPrivate)
            {
                existing.Channel.IsPrivate = false;
                changed = true;
            }

            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            {
                existing.Description = description;
                changed = true;
            }

            if (!string.Equals(existing.CtaLabel, ctaLabel, StringComparison.Ordinal))
            {
                existing.CtaLabel = ctaLabel;
                changed = true;
            }

            if (changed)
            {
                existing.Channel.UpdatedAtUtc = now;
                existing.UpdatedAtUtc = now;
                logger?.LogInformation(
                    "Updated default ticket portal '{DisplayName}' (filter {FilterName}) for {AccountClass}.",
                    displayName,
                    filterName,
                    ownerAccountClass);
            }

            return;
        }

        Guid channelId = Guid.NewGuid();
        string roomId = CustomChannelIds.BuildRoomId(channelId);

        db.CustomChannels.Add(new CustomChannel
        {
            ChannelId = channelId,
            RoomId = roomId,
            DisplayName = displayName,
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
            CtaLabel = ctaLabel,
            Description = description,
            Purpose = displayName,
            FilterName = filterName,
            NextDisplayNumber = 1,
            TrackingMode = trackingMode,
            TrackingInstructions = trackingInstructions,
            DecisionLabelsJson = TicketJson.SerializeStringList(decisionLabels),
            MentionRoleRulesJson = TicketJson.SerializeAccessRules(mentionRules),
            StaffAccessRulesJson = TicketJson.SerializeAccessRules(staffRules),
            IntakeSchemaJson = TicketJson.SerializeIntakeSchema(intake),
            UpdatedAtUtc = now,
        });

        logger?.LogInformation(
            "Seeded default ticket portal '{DisplayName}' (filter {FilterName}) for {AccountClass}.",
            displayName,
            filterName,
            ownerAccountClass);
    }
}
