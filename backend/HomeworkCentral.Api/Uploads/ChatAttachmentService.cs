using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeworkCentral.Api.Uploads;

public sealed record ChatAttachmentDto(
    Guid AttachmentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl);

public interface IChatAttachmentService
{
    Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class ChatAttachmentService(
    AppDbContext db,
    IAccessScopeAccessor accessScope,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IOptions<UploadOptions> options) : IChatAttachmentService
{
    public async Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    {
        UploadOptions opts = options.Value;
        if (file.Length <= 0 || file.Length > opts.MaxBytes)
            throw new InvalidOperationException($"File must be between 1 and {opts.MaxBytes} bytes.");

        string contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
        if (!opts.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Content type '{contentType}' is not allowed.");

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        System.Collections.BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);
        bool isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (isImage && !BitMask.HasBit(featureMask, PlatformFeatures.ImageUploads)
            && !BitMask.HasBit(featureMask, PlatformFeatures.FileUploads))
        {
            throw new InvalidOperationException("You do not have permission to upload images.");
        }

        if (!isImage && !BitMask.HasBit(featureMask, PlatformFeatures.FileUploads))
            throw new InvalidOperationException("You do not have permission to upload files.");

        AccessScope? scope = accessScope.ResolveCurrent()
            ?? throw new InvalidOperationException("Access scope is required.");

        Directory.CreateDirectory(opts.RootPath);
        Guid attachmentId = Guid.NewGuid();
        string safeName = Path.GetFileName(file.FileName);
        string storageName = $"{attachmentId:N}_{safeName}";
        string fullPath = Path.Combine(opts.RootPath, storageName);

        await using (FileStream fs = File.Create(fullPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        ChatAttachment attachment = new()
        {
            AttachmentId = attachmentId,
            UploadedByUserId = userId,
            OriginalFileName = safeName,
            ContentType = contentType,
            SizeBytes = file.Length,
            StoragePath = storageName,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = scope.AccountClass,
            TenantDatabaseName = scope.TenantDatabaseName,
        };
        db.ChatAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new ChatAttachmentDto(
            attachmentId,
            safeName,
            contentType,
            file.Length,
            $"/api/chat/attachments/{attachmentId}");
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default)
    {
        ChatAttachment? attachment = await db.ChatAttachments.AsNoTracking()
            .Include(a => a.MessageLinks)
            .ThenInclude(link => link.Message)
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
        if (attachment is null)
            return null;

        if (!await CanDownloadAsync(attachment, userId, ct))
            return null;

        UploadOptions opts = options.Value;
        string fullPath = Path.Combine(opts.RootPath, attachment.StoragePath);
        if (!File.Exists(fullPath))
            return null;

        Stream stream = File.OpenRead(fullPath);
        return (stream, attachment.ContentType, attachment.OriginalFileName);
    }

    private async Task<bool> CanDownloadAsync(ChatAttachment attachment, Guid userId, CancellationToken ct)
    {
        // Uploader may download an unattached (or attached) file they just uploaded.
        if (attachment.UploadedByUserId == userId)
            return true;

        List<string> roomIds = attachment.MessageLinks
            .Select(link => link.Message.RoomId)
            .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (roomIds.Count == 0)
            return false;

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        return roomIds.Any(roomId => chatRoomAccess.CanAccessRoom(masks, userId, roomId));
    }
}
