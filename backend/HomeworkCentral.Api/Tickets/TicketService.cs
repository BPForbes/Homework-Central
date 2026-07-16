using System.Text.Json;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tickets;

public sealed class TicketService(
    AppDbContext db,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    ICustomChannelStore channelStore,
    IChatNavNotifier chatNavNotifier,
    IAccessScopeAccessor accessScope,
    ITicketRecipientResolver recipientResolver,
    ITicketTrackingAnalyzer trackingAnalyzer) : ITicketService
{
    private static readonly Guid SystemInboxMessageId =
        Guid.Parse("00000000-0000-0000-0000-00000000c002");

    public async Task<TicketPortalConfigDto?> GetPortalConfigAsync(
        Guid channelId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        TicketPortalConfig? config = await db.TicketPortalConfigs
            .AsNoTracking()
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(p => p.ChannelId == channelId && !p.Channel.IsArchived, ct);

        if (config is null || !CanViewChannelScope(config.Channel))
            return null;

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(actorUserId, ct);
        return chatRoomAccess.CanAccessRoom(masks, actorUserId, config.Channel.RoomId)
            ? await MapPortalConfigAsync(config, ct)
            : null;
    }

    public async Task<TicketPortalConfigDto?> GetPortalConfigByRoomAsync(string roomId, CancellationToken ct = default)
    {
        TicketPortalConfig? config = await db.TicketPortalConfigs
            .AsNoTracking()
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(
                p => p.Channel.RoomId == roomId
                     && p.Channel.RoomType == CustomRoomType.Ticket
                     && !p.Channel.IsArchived,
                ct);

        return config is null || !CanViewChannelScope(config.Channel) ? null : await MapPortalConfigAsync(config, ct);
    }

    public async Task<TicketPortalConfigDto?> UpdatePortalConfigAsync(
        Guid channelId,
        Guid actorUserId,
        UpdateTicketPortalConfigRequest request,
        CancellationToken ct = default)
    {
        TicketPortalConfig? config = await db.TicketPortalConfigs
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(
                p => p.ChannelId == channelId
                     && p.Channel.RoomType == CustomRoomType.Ticket
                     && !p.Channel.IsArchived,
                ct);

        if (config is null || !CanViewChannelScope(config.Channel))
            return null;

        UserEffectiveMask mask = await effectiveMaskService.GetUserEffectiveMaskAsync(actorUserId, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(actorUserId, ct);
        if (!BitMask.HasBit(mask.EffectiveModerationMask, ModerationPermissions.ManageServerInfrastructure))
            throw new InvalidOperationException("You do not have permission to edit ticket portal configuration.");

        string purpose = request.Purpose.Trim();
        if (string.IsNullOrWhiteSpace(purpose))
            throw new InvalidOperationException("Purpose is required.");

        string trackingMode = request.TrackingMode.Trim();
        if (!IsValidTrackingMode(trackingMode))
            throw new InvalidOperationException("Invalid tracking mode.");

        TicketIntakeValidator.ValidateSchema(request.IntakeQuestions);
        ValidatePortalAccessRules(request.MentionRoleRules, "mention role");
        ValidatePortalAccessRules(request.StaffAccessRules, "staff access");

        config.CtaLabel = request.CtaLabel.Trim();
        config.Description = request.Description?.Trim() ?? string.Empty;
        config.Purpose = purpose;
        config.TrackingMode = trackingMode;
        config.TrackingInstructions = string.IsNullOrWhiteSpace(request.TrackingInstructions)
            ? null
            : request.TrackingInstructions.Trim();
        config.DecisionLabelsJson = TicketJson.SerializeStringList(request.DecisionLabels);
        config.MentionRoleRulesJson = TicketJson.SerializeAccessRules(request.MentionRoleRules);
        config.StaffAccessRulesJson = TicketJson.SerializeAccessRules(request.StaffAccessRules);
        config.IntakeSchemaJson = TicketJson.SerializeIntakeSchema(request.IntakeQuestions);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await MapPortalConfigAsync(config, ct);
    }

    public async Task<TicketDto> OpenTicketAsync(
        string portalRoomId,
        Guid openerUserId,
        OpenTicketRequest request,
        CancellationToken ct = default)
    {
        EffectiveMaskDto openerMasks = await effectiveMaskService.GetEffectiveMaskDtoAsync(openerUserId, ct);
        if (!chatRoomAccess.CanAccessRoom(openerMasks, openerUserId, portalRoomId))
            throw new InvalidOperationException("You cannot access this ticket portal.");

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(ct);

        TicketPortalConfig portal = await db.TicketPortalConfigs
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(
                p => p.Channel.RoomId == portalRoomId
                     && p.Channel.RoomType == CustomRoomType.Ticket
                     && !p.Channel.IsArchived,
                ct)
            ?? throw new InvalidOperationException("Ticket portal was not found.");

        if (!CanViewChannelScope(portal.Channel))
            throw new InvalidOperationException("Ticket portal is not available in your account scope.");

        List<TicketIntakeQuestionDto> schema = TicketJson.DeserializeIntakeSchema(portal.IntakeSchemaJson);
        TicketIntakeValidator.ValidateAnswers(schema, request.Answers);

        int displayNumber = portal.NextDisplayNumber;
        portal.NextDisplayNumber = checked(displayNumber + 1);
        portal.UpdatedAtUtc = DateTime.UtcNow;

        string purpose = portal.Purpose;
        string displayName = TicketDisplayNames.Open(purpose, displayNumber);
        DateTime now = DateTime.UtcNow;
        Guid chatChannelId = Guid.NewGuid();
        string roomId = CustomChannelIds.BuildRoomId(chatChannelId);
        CustomChannel portalChannel = portal.Channel;

        CustomChannel chatChannel = new()
        {
            ChannelId = chatChannelId,
            RoomId = roomId,
            DisplayName = displayName,
            CategoryKey = portalChannel.CategoryKey,
            CategoryDisplayName = portalChannel.CategoryDisplayName,
            RoomType = CustomRoomType.Chat,
            IsPrivate = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = openerUserId,
            OwnerAccountClass = portalChannel.OwnerAccountClass,
            TieType = ChannelTieType.None,
        };

        List<CustomChannelAccessRuleInput> staffRules =
            TicketJson.DeserializeAccessRules(portal.StaffAccessRulesJson);
        await ApplyTicketAccessRulesAsync(chatChannel, staffRules, ct);
        CustomChannelAccessRule openerRule = new()
        {
            AccessRuleId = Guid.NewGuid(),
            ChannelId = chatChannelId,
            AllowedUserId = openerUserId,
        };
        db.CustomChannelAccessRules.Add(openerRule);
        chatChannel.AccessRules.Add(openerRule);

        Ticket ticket = new()
        {
            TicketId = Guid.NewGuid(),
            PortalChannelId = portal.ChannelId,
            ChatChannelId = chatChannelId,
            RoomId = roomId,
            DisplayNumber = displayNumber,
            Purpose = purpose,
            OpenedByUserId = openerUserId,
            CreatedAtUtc = now,
            IntakeAnswersJson = TicketJson.SerializeStoredAnswers(request.Answers),
        };

        db.CustomChannels.Add(chatChannel);
        db.Tickets.Add(ticket);
        await CreateInitialWatchesAsync(ticket, portal, schema, request.Answers, openerUserId, now, ct);
        await db.SaveChangesAsync(ct);

        List<TicketIntakeAnswerDto> intakeAnswers = TicketJson.BuildIntakeAnswerDtos(schema, request.Answers);
        await CreateTicketOpenedInboxNotificationsAsync(
            ticket,
            chatChannel,
            portal,
            intakeAnswers,
            ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(chatChannel.OwnerAccountClass, ct);

        return await MapTicketAsync(ticket.TicketId, openerUserId, ct)
            ?? throw new InvalidOperationException("Ticket could not be loaded after creation.");
    }

    public async Task<TicketDto?> GetTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default) =>
        await LoadAccessibleTicketDtoAsync(ticketId, actorUserId, byRoomId: null, ct);

    public async Task<TicketDto?> GetTicketByRoomAsync(string roomId, Guid actorUserId, CancellationToken ct = default) =>
        await LoadAccessibleTicketDtoAsync(ticketId: null, actorUserId, roomId, ct);

    public async Task<TicketDto> CloseTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default)
    {
        Ticket ticket = await LoadAccessibleTicketForMutationAsync(ticketId, actorUserId, ct);
        if (ticket.ClosedAtUtc is not null)
            throw new InvalidOperationException("Ticket is already closed.");

        DateTime now = DateTime.UtcNow;
        ticket.ClosedAtUtc = now;
        ticket.ClosedByUserId = actorUserId;
        ticket.ChatChannel.DisplayName = TicketDisplayNames.Closed(ticket.Purpose, ticket.DisplayNumber);
        ticket.ChatChannel.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(ticket.ChatChannel.OwnerAccountClass, ct);

        return await MapTicketAsync(ticket.TicketId, actorUserId, ct)
            ?? throw new InvalidOperationException("Ticket could not be loaded after close.");
    }

    public async Task<TicketDto> ReopenTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default)
    {
        Ticket ticket = await LoadAccessibleTicketForMutationAsync(ticketId, actorUserId, ct);
        if (ticket.ClosedAtUtc is null)
            throw new InvalidOperationException("Ticket is not closed.");

        DateTime now = DateTime.UtcNow;
        ticket.ClosedAtUtc = null;
        ticket.ClosedByUserId = null;
        ticket.ChatChannel.DisplayName = TicketDisplayNames.Open(ticket.Purpose, ticket.DisplayNumber);
        ticket.ChatChannel.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(ticket.ChatChannel.OwnerAccountClass, ct);

        return await MapTicketAsync(ticket.TicketId, actorUserId, ct)
            ?? throw new InvalidOperationException("Ticket could not be loaded after reopen.");
    }

    public async Task<bool> DeleteTicketAsync(Guid ticketId, Guid actorUserId, CancellationToken ct = default)
    {
        Ticket ticket = await LoadAccessibleTicketForMutationAsync(ticketId, actorUserId, ct);
        if (ticket.ClosedAtUtc is null)
            throw new InvalidOperationException("Only closed tickets can be deleted.");

        AccountClass accountClass = ticket.ChatChannel.OwnerAccountClass;
        string roomId = ticket.RoomId;
        Guid chatChannelId = ticket.ChatChannelId;

        await db.ChatMentionNotifications
            .Where(notification => notification.TicketId == ticketId || notification.RoomId == roomId)
            .ExecuteDeleteAsync(ct);

        await db.ChatMessages
            .Where(message => message.RoomId == roomId)
            .ExecuteDeleteAsync(ct);

        await db.CustomChannels
            .Where(channel => channel.ChannelId == chatChannelId)
            .ExecuteDeleteAsync(ct);

        await channelStore.RefreshAsync(ct);
        await chatNavNotifier.NotifyNavChangedAsync(accountClass, ct);
        return true;
    }

    public async Task<TicketUserWatchDto?> UpsertWatchAsync(
        Guid ticketId,
        Guid actorUserId,
        UpsertTicketWatchRequest request,
        CancellationToken ct = default)
    {
        Ticket ticket = await LoadAccessibleTicketForMutationAsync(ticketId, actorUserId, ct);
        if (ticket.ClosedAtUtc is not null)
            throw new InvalidOperationException("Watches can only be updated on open tickets.");

        bool userExists = await db.Users.AsNoTracking().AnyAsync(u => u.UserId == request.TrackedUserId, ct);
        if (!userExists)
            throw new InvalidOperationException("Tracked user was not found.");

        string contextLabel = string.IsNullOrWhiteSpace(request.ContextLabel)
            ? "Staff watch"
            : request.ContextLabel.Trim();
        if (contextLabel.Length > 128)
            contextLabel = contextLabel[..128];

        DateTime now = DateTime.UtcNow;
        TicketUserWatch? existing = await db.TicketUserWatches
            .FirstOrDefaultAsync(
                watch => watch.TicketId == ticketId && watch.TrackedUserId == request.TrackedUserId,
                ct);

        if (existing is null)
        {
            existing = new TicketUserWatch
            {
                WatchId = Guid.NewGuid(),
                TicketId = ticketId,
                TrackedUserId = request.TrackedUserId,
                ContextLabel = contextLabel,
                IsActive = request.IsActive,
                SetByUserId = actorUserId,
                UpdatedAtUtc = now,
                Source = TicketWatchSources.Staff,
            };
            db.TicketUserWatches.Add(existing);
        }
        else
        {
            existing.ContextLabel = contextLabel;
            existing.IsActive = request.IsActive;
            existing.SetByUserId = actorUserId;
            existing.UpdatedAtUtc = now;
            existing.Source = TicketWatchSources.Staff;
        }

        await db.SaveChangesAsync(ct);
        Dictionary<Guid, string> usernames = await LoadUsernamesAsync([request.TrackedUserId], ct);
        return MapWatch(existing, usernames);
    }

    public async Task<TicketAnalyzeResultDto> AnalyzeAsync(
        Guid ticketId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        Ticket ticket = await LoadAccessibleTicketForMutationAsync(ticketId, actorUserId, ct);
        if (ticket.ClosedAtUtc is not null)
            throw new InvalidOperationException("Analysis is only available for open tickets.");

        TicketPortalConfig portal = ticket.Portal
            ?? await db.TicketPortalConfigs.AsNoTracking()
                .FirstAsync(p => p.ChannelId == ticket.PortalChannelId, ct);

        List<ChatMessage> messages = await db.ChatMessages
            .AsNoTracking()
            .Where(message => message.RoomId == ticket.RoomId)
            .OrderBy(message => message.CreatedAtUtc)
            .ToListAsync(ct);

        TicketAnalysisResult analysis = await trackingAnalyzer.AnalyzeAsync(portal, ticket, messages, ct);
        if (!analysis.Available)
        {
            return new TicketAnalyzeResultDto
            {
                Available = false,
                Watches = await MapWatchesAsync(ticket.TicketId, ct),
            };
        }

        if (analysis.TrackedUserId is Guid trackedUserId)
        {
            await UpsertModelWatchAsync(ticket, trackedUserId, analysis.Summary, actorUserId, ct);
        }

        int notified = 0;
        if (!string.IsNullOrWhiteSpace(analysis.Decision))
        {
            notified = await CreateTicketDecisionInboxNotificationsAsync(
                ticket,
                ticket.ChatChannel,
                portal,
                analysis.Decision,
                analysis.Summary,
                ct);
            await db.SaveChangesAsync(ct);
        }

        return new TicketAnalyzeResultDto
        {
            Available = true,
            Decision = analysis.Decision,
            Summary = analysis.Summary,
            Watches = await MapWatchesAsync(ticket.TicketId, ct),
            InboxRecipientsNotified = notified,
        };
    }

    private async Task<TicketDto?> LoadAccessibleTicketDtoAsync(
        Guid? ticketId,
        Guid actorUserId,
        string? byRoomId,
        CancellationToken ct)
    {
        Ticket? ticket = ticketId.HasValue
            ? await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticketId.Value, ct)
            : await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.RoomId == byRoomId, ct);

        if (ticket is null)
            return null;

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(actorUserId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, actorUserId, ticket.RoomId))
            return null;

        return await MapTicketAsync(ticket.TicketId, actorUserId, ct);
    }

    private async Task<Ticket> LoadAccessibleTicketForMutationAsync(
        Guid ticketId,
        Guid actorUserId,
        CancellationToken ct)
    {
        Ticket ticket = await db.Tickets
            .Include(t => t.ChatChannel)
            .Include(t => t.Portal)
            .Include(t => t.Watches)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId, ct)
            ?? throw new InvalidOperationException("Ticket was not found.");

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(actorUserId, ct);
        if (!chatRoomAccess.CanAccessRoom(masks, actorUserId, ticket.RoomId))
            throw new InvalidOperationException("You cannot access this ticket.");

        if (!CanManageTicket(ticket, actorUserId, masks))
            throw new InvalidOperationException("You do not have permission to manage this ticket.");

        return ticket;
    }

    private async Task CreateInitialWatchesAsync(
        Ticket ticket,
        TicketPortalConfig portal,
        IReadOnlyList<TicketIntakeQuestionDto> schema,
        IReadOnlyDictionary<string, JsonElement> answers,
        Guid openerUserId,
        DateTime now,
        CancellationToken ct)
    {
        if (string.Equals(portal.TrackingMode, TicketTrackingModes.Opener, StringComparison.Ordinal))
        {
            AddWatchEntity(ticket, openerUserId, "Ticket opener", TicketWatchSources.Intake, openerUserId, now);
        }

        if (string.Equals(portal.TrackingMode, TicketTrackingModes.FromIntakeField, StringComparison.Ordinal))
        {
            foreach (TicketIntakeQuestionDto question in schema.Where(q => q.TracksUser))
            {
                if (!answers.TryGetValue(question.Id, out JsonElement value))
                    continue;

                if (!TicketIntakeValidator.TryParseUserId(value, out Guid trackedUserId))
                    continue;

                AddWatchEntity(
                    ticket,
                    trackedUserId,
                    question.Prompt,
                    TicketWatchSources.Intake,
                    openerUserId,
                    now);
            }
        }
    }

    private static void AddWatchEntity(
        Ticket ticket,
        Guid trackedUserId,
        string contextLabel,
        string source,
        Guid setByUserId,
        DateTime now)
    {
        if (ticket.Watches.Any(watch => watch.TrackedUserId == trackedUserId))
            return;

        ticket.Watches.Add(new TicketUserWatch
        {
            WatchId = Guid.NewGuid(),
            TicketId = ticket.TicketId,
            TrackedUserId = trackedUserId,
            ContextLabel = contextLabel.Length > 128 ? contextLabel[..128] : contextLabel,
            IsActive = true,
            SetByUserId = setByUserId,
            UpdatedAtUtc = now,
            Source = source,
        });
    }

    private async Task UpsertModelWatchAsync(
        Ticket ticket,
        Guid trackedUserId,
        string? summary,
        Guid actorUserId,
        CancellationToken ct)
    {
        bool userExists = await db.Users.AsNoTracking().AnyAsync(u => u.UserId == trackedUserId, ct);
        if (!userExists)
            return;

        string contextLabel = string.IsNullOrWhiteSpace(summary) ? "Model tracking" : summary.Trim();
        if (contextLabel.Length > 128)
            contextLabel = contextLabel[..128];

        DateTime now = DateTime.UtcNow;
        TicketUserWatch? existing = ticket.Watches.FirstOrDefault(watch => watch.TrackedUserId == trackedUserId);
        if (existing is null)
        {
            existing = new TicketUserWatch
            {
                WatchId = Guid.NewGuid(),
                TicketId = ticket.TicketId,
                TrackedUserId = trackedUserId,
                ContextLabel = contextLabel,
                IsActive = true,
                SetByUserId = actorUserId,
                UpdatedAtUtc = now,
                Source = TicketWatchSources.Model,
            };
            db.TicketUserWatches.Add(existing);
            ticket.Watches.Add(existing);
        }
        else
        {
            existing.ContextLabel = contextLabel;
            existing.IsActive = true;
            existing.SetByUserId = actorUserId;
            existing.UpdatedAtUtc = now;
            existing.Source = TicketWatchSources.Model;
        }
    }

    private async Task CreateTicketOpenedInboxNotificationsAsync(
        Ticket ticket,
        CustomChannel chatChannel,
        TicketPortalConfig portal,
        IReadOnlyList<TicketIntakeAnswerDto> intakeAnswers,
        CancellationToken ct)
    {
        List<CustomChannelAccessRuleInput> mentionRules =
            TicketJson.DeserializeAccessRules(portal.MentionRoleRulesJson);

        AccessScope? scope = accessScope.ResolveCurrent();
        string? tenantDatabaseName = scope?.TenantDatabaseName;
        HashSet<Guid> recipients = await recipientResolver.ResolveRecipientsAsync(
            mentionRules,
            ticket.RoomId,
            chatChannel.OwnerAccountClass,
            tenantDatabaseName,
            ct);
        recipients.Add(ticket.OpenedByUserId);

        string payloadJson = TicketJson.SerializeOpenedPayload(new TicketOpenedPayloadDto
        {
            IntakeAnswers = intakeAnswers.ToList(),
        });

        DateTime now = DateTime.UtcNow;
        string messageContent = $"Ticket opened: {chatChannel.DisplayName}";

        foreach (Guid recipientId in recipients)
        {
            db.ChatMentionNotifications.Add(new ChatMentionNotification
            {
                NotificationId = Guid.NewGuid(),
                MessageId = SystemInboxMessageId,
                RecipientUserId = recipientId,
                SenderId = TicketSystemAuthor.SenderId,
                SenderUsername = TicketSystemAuthor.DisplayName,
                RoomId = ticket.RoomId,
                RoomDisplayName = chatChannel.DisplayName,
                CategoryKey = chatChannel.CategoryKey,
                CategoryDisplayName = chatChannel.CategoryDisplayName,
                MessageContent = messageContent,
                MentionKind = TicketMentionKinds.Opened,
                TicketId = ticket.TicketId,
                TicketPayloadJson = payloadJson,
                CreatedAtUtc = now,
                OwnerAccountClass = chatChannel.OwnerAccountClass,
                TenantDatabaseName = tenantDatabaseName,
            });
        }
    }

    private async Task<int> CreateTicketDecisionInboxNotificationsAsync(
        Ticket ticket,
        CustomChannel chatChannel,
        TicketPortalConfig portal,
        string decision,
        string? summary,
        CancellationToken ct)
    {
        List<CustomChannelAccessRuleInput> mentionRules =
            TicketJson.DeserializeAccessRules(portal.MentionRoleRulesJson);

        AccessScope? scope = accessScope.ResolveCurrent();
        string? tenantDatabaseName = scope?.TenantDatabaseName;
        HashSet<Guid> recipients = await recipientResolver.ResolveRecipientsAsync(
            mentionRules,
            ticket.RoomId,
            chatChannel.OwnerAccountClass,
            tenantDatabaseName,
            ct);

        if (recipients.Count == 0)
            return 0;

        string payloadJson = TicketJson.SerializeDecisionPayload(new TicketDecisionPayloadDto
        {
            Decision = decision,
            Summary = summary,
        });

        DateTime now = DateTime.UtcNow;
        string messageContent = string.IsNullOrWhiteSpace(summary)
            ? $"Ticket decision: {decision}"
            : $"{decision}: {summary}";

        foreach (Guid recipientId in recipients)
        {
            db.ChatMentionNotifications.Add(new ChatMentionNotification
            {
                NotificationId = Guid.NewGuid(),
                MessageId = SystemInboxMessageId,
                RecipientUserId = recipientId,
                SenderId = TicketSystemAuthor.SenderId,
                SenderUsername = TicketSystemAuthor.DisplayName,
                RoomId = ticket.RoomId,
                RoomDisplayName = chatChannel.DisplayName,
                CategoryKey = chatChannel.CategoryKey,
                CategoryDisplayName = chatChannel.CategoryDisplayName,
                MessageContent = messageContent,
                MentionKind = TicketMentionKinds.Decision,
                TicketId = ticket.TicketId,
                TicketPayloadJson = payloadJson,
                CreatedAtUtc = now,
                OwnerAccountClass = chatChannel.OwnerAccountClass,
                TenantDatabaseName = tenantDatabaseName,
            });
        }

        return recipients.Count;
    }

    private async Task ApplyTicketAccessRulesAsync(
        CustomChannel channel,
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        CancellationToken ct)
    {
        AccessScope scope = RequireScope();

        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            ValidateSingleAccessRule(rule);

            if (rule.CustomRoleId is Guid customRoleId)
            {
                Role? customRole = await db.Roles.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RoleId == customRoleId && r.IsCustom, ct);
                if (customRole is null)
                    throw new InvalidOperationException("Custom access role was not found.");

                if (!InfrastructureAccountScope.CanViewInfrastructure(scope, customRole.OwnerAccountClass))
                {
                    throw new InvalidOperationException(
                        "Custom access roles must belong to the same account scope as the room.");
                }
            }

            CustomChannelAccessRule newRule = new()
            {
                AccessRuleId = Guid.NewGuid(),
                ChannelId = channel.ChannelId,
                CustomRoleId = rule.CustomRoleId,
                PlatformRoleBit = rule.PlatformRoleBit,
                AllowedUserId = rule.AllowedUserId,
            };
            db.CustomChannelAccessRules.Add(newRule);
            channel.AccessRules.Add(newRule);
        }
    }

    private async Task<TicketPortalConfigDto> MapPortalConfigAsync(TicketPortalConfig config, CancellationToken ct)
    {
        List<CustomChannelAccessRuleInput> mentionRules =
            TicketJson.DeserializeAccessRules(config.MentionRoleRulesJson);
        List<CustomChannelAccessRuleInput> staffRules =
            TicketJson.DeserializeAccessRules(config.StaffAccessRulesJson);

        return new TicketPortalConfigDto
        {
            ChannelId = config.ChannelId,
            RoomId = config.Channel.RoomId,
            CtaLabel = config.CtaLabel,
            Description = config.Description,
            Purpose = config.Purpose,
            NextDisplayNumber = config.NextDisplayNumber,
            TrackingMode = config.TrackingMode,
            TrackingInstructions = config.TrackingInstructions,
            DecisionLabels = TicketJson.DeserializeStringList(config.DecisionLabelsJson),
            MentionRoleRules = await MapAccessRuleDtosAsync(mentionRules, ct),
            StaffAccessRules = await MapAccessRuleDtosAsync(staffRules, ct),
            IntakeQuestions = TicketJson.DeserializeIntakeSchema(config.IntakeSchemaJson),
        };
    }

    private async Task<List<CustomChannelAccessRuleDto>> MapAccessRuleDtosAsync(
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        CancellationToken ct)
    {
        List<Guid> customRoleIds = rules
            .Where(rule => rule.CustomRoleId.HasValue)
            .Select(rule => rule.CustomRoleId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> roleNames = customRoleIds.Count == 0
            ? []
            : await db.Roles.AsNoTracking()
                .Where(role => customRoleIds.Contains(role.RoleId))
                .ToDictionaryAsync(role => role.RoleId, role => role.Name, ct);

        List<CustomChannelAccessRuleDto> result = [];
        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            result.Add(new CustomChannelAccessRuleDto
            {
                CustomRoleId = rule.CustomRoleId,
                CustomRoleName = rule.CustomRoleId is Guid roleId && roleNames.TryGetValue(roleId, out string? name)
                    ? name
                    : null,
                PlatformRoleBit = rule.PlatformRoleBit,
                PlatformRoleName = rule.PlatformRoleBit is short bit
                                   && PlatformRoleCatalog.TryGetRoleNameFromBit(bit, out string? platformName)
                    ? platformName
                    : null,
                AllowedUserId = rule.AllowedUserId,
            });
        }

        return result;
    }

    private async Task<TicketDto?> MapTicketAsync(
        Guid ticketId,
        Guid actorUserId,
        CancellationToken ct)
    {
        Ticket? ticket = await db.Tickets
            .AsNoTracking()
            .Include(t => t.ChatChannel)
            .Include(t => t.Portal)
            .ThenInclude(p => p!.Channel)
            .Include(t => t.Watches)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);

        if (ticket is null)
            return null;

        List<TicketIntakeQuestionDto> schema =
            TicketJson.DeserializeIntakeSchema(ticket.Portal.IntakeSchemaJson);
        Dictionary<string, JsonElement> answers = TicketJson.DeserializeStoredAnswers(ticket.IntakeAnswersJson);

        HashSet<Guid> userIds = [ticket.OpenedByUserId, .. ticket.Watches.Select(w => w.TrackedUserId)];
        Dictionary<Guid, string> usernames = await LoadUsernamesAsync(userIds, ct);
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(actorUserId, ct);

        return new TicketDto
        {
            TicketId = ticket.TicketId,
            PortalChannelId = ticket.PortalChannelId,
            PortalRoomId = ticket.Portal.Channel.RoomId,
            ChatChannelId = ticket.ChatChannelId,
            RoomId = ticket.RoomId,
            DisplayName = ticket.ChatChannel.DisplayName,
            Purpose = ticket.Purpose,
            DisplayNumber = ticket.DisplayNumber,
            Status = ticket.ClosedAtUtc is null ? TicketStatuses.Open : TicketStatuses.Closed,
            OpenedByUserId = ticket.OpenedByUserId,
            OpenedByUsername = usernames.GetValueOrDefault(ticket.OpenedByUserId, "Unknown"),
            CanManage = CanManageTicket(ticket, actorUserId, masks),
            CreatedAtUtc = ticket.CreatedAtUtc,
            ClosedAtUtc = ticket.ClosedAtUtc,
            ClosedByUserId = ticket.ClosedByUserId,
            IntakeAnswers = TicketJson.BuildIntakeAnswerDtos(schema, answers),
            Watches = ticket.Watches
                .OrderByDescending(watch => watch.UpdatedAtUtc)
                .Select(watch => MapWatch(watch, usernames))
                .ToList(),
        };
    }

    private async Task<List<TicketUserWatchDto>> MapWatchesAsync(Guid ticketId, CancellationToken ct)
    {
        List<TicketUserWatch> watches = await db.TicketUserWatches
            .AsNoTracking()
            .Where(watch => watch.TicketId == ticketId)
            .OrderByDescending(watch => watch.UpdatedAtUtc)
            .ToListAsync(ct);

        HashSet<Guid> userIds = watches.Select(watch => watch.TrackedUserId).ToHashSet();
        Dictionary<Guid, string> usernames = await LoadUsernamesAsync(userIds, ct);
        return watches.Select(watch => MapWatch(watch, usernames)).ToList();
    }

    private static TicketUserWatchDto MapWatch(TicketUserWatch watch, IReadOnlyDictionary<Guid, string> usernames) =>
        new()
        {
            WatchId = watch.WatchId,
            TrackedUserId = watch.TrackedUserId,
            TrackedUsername = usernames.GetValueOrDefault(watch.TrackedUserId, "Unknown"),
            ContextLabel = watch.ContextLabel,
            IsActive = watch.IsActive,
            Source = watch.Source,
            UpdatedAtUtc = watch.UpdatedAtUtc,
        };

    private async Task<Dictionary<Guid, string>> LoadUsernamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        return await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.Username, ct);
    }

    private static void ValidatePortalAccessRules(
        IReadOnlyList<CustomChannelAccessRuleInput> rules,
        string label)
    {
        foreach (CustomChannelAccessRuleInput rule in rules)
        {
            try
            {
                ValidateSingleAccessRule(rule);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{label}: {ex.Message}");
            }
        }
    }

    private static void ValidateSingleAccessRule(CustomChannelAccessRuleInput rule)
    {
        int setCount = (rule.CustomRoleId.HasValue ? 1 : 0)
            + (rule.PlatformRoleBit.HasValue ? 1 : 0)
            + (rule.AllowedUserId.HasValue ? 1 : 0);

        if (setCount != 1)
        {
            throw new InvalidOperationException(
                "Each access rule must specify exactly one of custom role, platform role, or allowed user.");
        }
    }

    private static bool IsValidTrackingMode(string trackingMode) =>
        string.Equals(trackingMode, TicketTrackingModes.None, StringComparison.Ordinal)
        || string.Equals(trackingMode, TicketTrackingModes.Opener, StringComparison.Ordinal)
        || string.Equals(trackingMode, TicketTrackingModes.FromIntakeField, StringComparison.Ordinal);

    private static bool CanManageTicket(Ticket ticket, Guid actorUserId, EffectiveMaskDto masks)
    {
        if (HasElevatedRoomAccess(masks))
            return true;

        foreach (CustomChannelAccessRuleInput rule in TicketJson.DeserializeAccessRules(ticket.Portal.StaffAccessRulesJson))
        {
            if (rule.AllowedUserId == actorUserId)
                return true;

            if (rule.PlatformRoleBit is short platformRoleBit
                && BitMask.HasBit(BitMask.FromBase64(masks.RoleMask, 64), platformRoleBit))
            {
                return true;
            }

            if (rule.CustomRoleId is Guid customRoleId && masks.CustomRoleIds.Contains(customRoleId))
                return true;
        }

        return false;
    }

    private static bool HasElevatedRoomAccess(EffectiveMaskDto masks) =>
        BitMask.HasBit(BitMask.FromBase64(masks.RoleMask, 64), PlatformRoles.Owner)
        || BitMask.HasBit(BitMask.FromBase64(masks.RoleMask, 64), PlatformRoles.Administrator)
        || BitMask.HasBit(BitMask.FromBase64(masks.RoleMask, 64), PlatformRoles.SystemAdministrator);

    private bool CanViewChannelScope(CustomChannel channel)
    {
        AccessScope? scope = accessScope.ResolveCurrent();
        return scope is not null
            && InfrastructureAccountScope.CanViewInfrastructure(scope, channel.OwnerAccountClass);
    }

    private AccessScope RequireScope() =>
        accessScope.ResolveCurrent()
        ?? throw new InvalidOperationException("Authenticated account scope is required.");
}
