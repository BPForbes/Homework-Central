using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeworkCentral.Api.Tickets;

/// <summary>
/// Ensures the two default ticket portals exist (Tutor applications + Notify Mods).
/// Idempotent: skips portals whose filter name (or legacy purpose/display name) already exists.
/// </summary>
public static class TicketPortalSeedData
{
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
            description: "Apply for a trial tutor position. Head tutors review applications in a private ticket.",
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
            description: "Report a user or incident to moderators. Include proof and the user being reported.",
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
        bool exists = await db.TicketPortalConfigs
            .IgnoreQueryFilters()
            .AnyAsync(
                p => !p.Channel.IsArchived
                     && p.Channel.OwnerAccountClass == ownerAccountClass
                     && (p.FilterName == filterName
                         || p.Purpose == filterName
                         || p.Channel.DisplayName == displayName),
                ct);

        if (exists)
            return;

        DateTime now = DateTime.UtcNow;
        Guid channelId = Guid.NewGuid();
        string roomId = CustomChannelIds.BuildRoomId(channelId);

        db.CustomChannels.Add(new CustomChannel
        {
            ChannelId = channelId,
            RoomId = roomId,
            DisplayName = displayName,
            IconName = "ticket",
            CategoryKey = "tickets",
            CategoryDisplayName = "Tickets",
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
