using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeworkCentral.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(
    IChatRoomAccessService chatRoomAccess,
    IEffectiveMaskService effectiveMaskService,
    IChatMessageService chatMessageService,
    IChatRoomDetailService chatRoomDetailService,
    IRoleAppearanceService roleAppearanceService,
    IChatMessageVoteService voteService,
    Uploads.IChatAttachmentService attachmentService,
    Uploads.IAttachmentAccessTokenService accessTokenService,
    InfrastructureUserDirectory userDirectory) : ControllerBase
{
    /// <summary>Returns chat navigation categories and rooms visible to the current user.</summary>
    [HttpGet("nav")]
    public async Task<ActionResult<ChatNavDto>> GetNav(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId.Value, ct);
        return Ok(chatRoomAccess.GetAccessibleNav(masks, userId.Value));
    }

    /// <summary>Returns room metadata for catalog and custom rooms (chat, info, role claim).</summary>
    [HttpGet("rooms/{roomId}")]
    public async Task<ActionResult<ChatRoomDetailDto>> GetRoom(string roomId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        string decodedRoomId = Uri.UnescapeDataString(roomId);
        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId.Value, ct);

        if (!chatRoomAccess.CanAccessRoom(masks, userId.Value, decodedRoomId))
            return Forbid();

        ChatRoomDetailDto? room = await chatRoomDetailService.GetRoomAsync(decodedRoomId, masks, ct);
        return room is null ? NotFound() : Ok(room);
    }

    /// <summary>Returns recent messages for a chat room.</summary>
    [HttpGet("rooms/{roomId}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        string roomId,
        [FromQuery] DateTime? beforeUtc,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        string decodedRoomId = Uri.UnescapeDataString(roomId);
        if (!await chatMessageService.CanAccessRoomAsync(decodedRoomId, userId.Value, ct))
            return Forbid();

        IReadOnlyList<ChatMessageDto> messages = await chatMessageService.GetMessagesAsync(
            decodedRoomId,
            userId.Value,
            beforeUtc,
            limit,
            ct);
        return Ok(messages);
    }

    /// <summary>Returns mentionable roles (name + color) for @ autocomplete in chat.</summary>
    [HttpGet("mention-roles")]
    public async Task<ActionResult<IReadOnlyList<MentionRoleOptionDto>>> GetMentionRoles(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await roleAppearanceService.GetMentionableRolesAsync(ct));
    }

    /// <summary>
    /// Prefix user lookup for mentions and ticket intake. Available to any authenticated user
    /// (unlike /api/infrastructure/users/search which requires ManageServerInfrastructure).
    /// Reuses <see cref="InfrastructureUserDirectory"/> rather than duplicating search logic.
    /// </summary>
    [HttpGet("users/search")]
    public async Task<ActionResult<IReadOnlyList<ChatUserLookupDto>>> SearchUsers(
        [FromQuery] string q,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        IReadOnlyList<(User User, string? TenantDatabaseName)> hits =
            await userDirectory.SearchUsersAsync(q ?? string.Empty, ct);

        return Ok(hits.Select(entry => new ChatUserLookupDto
        {
            UserId = entry.User.UserId,
            Username = entry.User.Username,
            Email = entry.User.Email,
        }).ToList());
    }

    /// <summary>Sends a message to a chat room.</summary>
    [HttpPost("rooms/{roomId}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(
        string roomId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken ct = default)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool hasContent = !string.IsNullOrWhiteSpace(request.Content);
        bool hasAttachments = request.AttachmentIds is { Count: > 0 };
        bool hasForward = request.ForwardedFrom is not null;
        if (!hasContent && !hasAttachments && !hasForward)
            return BadRequest(new { message = "Message content, attachment, or forward is required." });

        string decodedRoomId = Uri.UnescapeDataString(roomId);
        if (!await chatMessageService.CanAccessRoomAsync(decodedRoomId, userId.Value, ct))
            return Forbid();

        ChatMessageDto? message;
        try
        {
            message = await chatMessageService.SendMessageAsync(
                decodedRoomId,
                userId.Value,
                request.Content ?? string.Empty,
                request.ReplyToMessageId,
                ct,
                request.AttachmentIds,
                request.ForwardedFrom);
        }
        catch (SendMessageMentionException mentionError)
        {
            return mentionError.Error switch
            {
                SendMessageMentionError.GuestCannotMention => StatusCode(
                    StatusCodes.Status403Forbidden,
                    new SendChatMessageErrorResponse
                    {
                        Message = "Guests cannot use @mentions.",
                        Code = "guest_cannot_mention",
                    }),
                SendMessageMentionError.MentionCooldown => StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new SendChatMessageErrorResponse
                    {
                        Message = "Please wait before mentioning again.",
                        Code = "mention_cooldown",
                        RetryAfterSeconds = (int)Math.Ceiling((mentionError.RetryAfter ?? TimeSpan.FromSeconds(3)).TotalSeconds),
                    }),
                _ => BadRequest(new { message = "Message content is invalid." }),
            };
        }
        catch (InvalidOperationException ex)
        {
            // Missing, foreign-owned, or cross-scope attachment IDs fail the send before save.
            return BadRequest(new { message = ex.Message });
        }

        // Access was already confirmed above, so a null result here can only mean the
        // (non-whitespace) content was rejected for another reason, e.g. exceeding the max
        // length — a client input problem, not an authorization one.
        if (message is null)
            return BadRequest(new { message = "Message content is invalid." });

        return Ok(message);
    }

    /// <summary>Returns mention notifications for the current user's inbox.</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<IReadOnlyList<ChatInboxItemDto>>> GetInbox(
        [FromQuery] string? categoryKey,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await chatMessageService.GetInboxAsync(userId.Value, categoryKey, ct));
    }

    /// <summary>Returns unread mention counts grouped by chat category.</summary>
    [HttpGet("inbox/summary")]
    public async Task<ActionResult<ChatInboxSummaryDto>> GetInboxSummary(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        return Ok(await chatMessageService.GetInboxSummaryAsync(userId.Value, ct));
    }

    [HttpPost("inbox/{notificationId:guid}/read")]
    public async Task<IActionResult> MarkInboxRead(Guid notificationId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        bool updated = await chatMessageService.MarkInboxReadAsync(userId.Value, notificationId, ct);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("inbox/read-all")]
    public async Task<IActionResult> MarkInboxAllRead(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        await chatMessageService.MarkInboxAllReadAsync(userId.Value, ct);
        return NoContent();
    }

    [HttpPost("inbox/delete")]
    public async Task<IActionResult> DeleteInboxItems(
        [FromBody] DeleteInboxItemsRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        if (request.NotificationIds is null || request.NotificationIds.Count == 0)
            return BadRequest(new { message = "Select at least one inbox item to delete." });

        await chatMessageService.DeleteInboxItemsAsync(userId.Value, request.NotificationIds, ct);
        return NoContent();
    }

    [HttpDelete("inbox")]
    public async Task<IActionResult> DeleteInboxAll(
        [FromQuery] string? categoryKey,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(categoryKey))
            await chatMessageService.DeleteInboxAllAsync(userId.Value, ct);
        else
            await chatMessageService.DeleteInboxCategoryAsync(userId.Value, categoryKey, ct);

        return NoContent();
    }

    [HttpPost("messages/{messageId:guid}/vote")]
    public async Task<ActionResult<MessageVoteDto>> CastVote(
        Guid messageId,
        [FromBody] CastMessageVoteRequest request,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            MessageVoteDto? result = await voteService.CastVoteAsync(
                messageId,
                userId.Value,
                (short)request.Value,
                ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("attachments")]
    [RequestSizeLimit(12_000_000)]
    public async Task<ActionResult<Uploads.ChatAttachmentDto>> UploadAttachment(
        IFormFile file,
        CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();
        if (file is null)
            return BadRequest(new { message = "File is required." });

        try
        {
            return Ok(await attachmentService.UploadAsync(userId.Value, file, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("attachments/{attachmentId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadAttachment(
        Guid attachmentId,
        [FromQuery] string? accessToken,
        [FromQuery] bool riskAcknowledged = false,
        CancellationToken ct = default)
    {
        Guid? userId = GetUserId();
        bool accessTokenValidated = false;

        switch (userId)
        {
            case null when string.IsNullOrWhiteSpace(accessToken):
                return Unauthorized();
            case null:
            {
                bool valid = await accessTokenService.TryValidateAsync(attachmentId, accessToken!, ct);
                if (!valid)
                    return Unauthorized();
                accessTokenValidated = true;
                userId = Guid.Empty;
                break;
            }
        }

        Uploads.AttachmentReadResult? opened =
            await attachmentService.OpenReadAsync(
                attachmentId,
                userId!.Value,
                ct,
                accessTokenValidated,
                riskAcknowledged);
        if (opened is null)
            return NotFound();

        if (opened.RequiresCaution)
        {
            return Conflict(new
            {
                message = "This attachment needs a safety acknowledgement before it can be opened.",
                scanStatus = opened.ScanStatus.ToString(),
            });
        }

        return File(opened.Stream!, opened.ContentType!, opened.FileName!);
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid attachmentId, CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            bool deleted = await attachmentService.DeleteUnattachedAsync(attachmentId, userId.Value, ct);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? GetUserId()
    {
        string? userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return userIdClaim is not null && Guid.TryParse(userIdClaim, out Guid userId)
            ? userId
            : null;
    }
}
