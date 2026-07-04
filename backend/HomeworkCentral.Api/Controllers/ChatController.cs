using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.DTOs;
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
    IChatMessageService chatMessageService) : ControllerBase
{
    /// <summary>Returns chat navigation categories and rooms visible to the current user.</summary>
    [HttpGet("nav")]
    public async Task<ActionResult<ChatNavDto>> GetNav(CancellationToken ct)
    {
        Guid? userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        UserEffectiveMask? mask = await effectiveMaskService.GetUserEffectiveMaskAsync(userId.Value, ct)
            ?? await effectiveMaskService.RebuildUserEffectiveMaskAsync(userId.Value, ct);

        EffectiveMaskDto masks = mask.ToEffectiveMaskDto();
        return Ok(chatRoomAccess.GetAccessibleNav(masks));
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

        // [Required]/[StringLength] on SendChatMessageRequest.Content already rejects null,
        // empty, and oversized payloads before this action runs, but a whitespace-only string
        // (e.g. "   ") passes those attributes, so it's still checked explicitly here — this
        // way a bad-content rejection is always a 400, never conflated with the 403 below.
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Message content cannot be empty." });

        string decodedRoomId = Uri.UnescapeDataString(roomId);
        if (!await chatMessageService.CanAccessRoomAsync(decodedRoomId, userId.Value, ct))
            return Forbid();

        ChatMessageDto? message;
        try
        {
            message = await chatMessageService.SendMessageAsync(
                decodedRoomId,
                userId.Value,
                request.Content,
                ct);
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

        // Access was already confirmed above, so a null result here can only mean the
        // (non-whitespace) content was rejected for another reason, e.g. exceeding the max
        // length — a client input problem, not an authorization one.
        if (message is null)
            return BadRequest(new { message = "Message content is invalid." });

        return Ok(message);
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
