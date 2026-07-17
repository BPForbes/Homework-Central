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
    string DownloadUrl,
    bool IsHazard,
    string? InlinePreviewKind);

public interface IChatAttachmentService
{
    Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default,
        bool accessTokenValidated = false);
    Task<bool> DeleteUnattachedAsync(Guid attachmentId, Guid userId, CancellationToken ct = default);
}

public sealed class ChatAttachmentService(
    AppDbContext db,
    IAccessScopeAccessor accessScope,
    IEffectiveMaskService effectiveMaskService,
    IChatRoomAccessService chatRoomAccess,
    IAttachmentTypeInspector typeInspector,
    IMalwareScanner malwareScanner,
    IAttachmentAccessTokenService accessTokenService,
    IOptions<UploadOptions> options) : IChatAttachmentService
{
    public async Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    {
        UploadOptions opts = options.Value;
        if (file.Length <= 0 || file.Length > opts.MaxBytes)
            throw new InvalidOperationException($"File must be between 1 and {opts.MaxBytes} bytes.");

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        System.Collections.BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);

        AccessScope? scope = accessScope.ResolveCurrent()
            ?? throw new InvalidOperationException("Access scope is required.");

        AttachmentTypeInspectionResult inspection;
        await using (Stream inspectStream = file.OpenReadStream())
        {
            inspection = typeInspector.Inspect(inspectStream, file.ContentType);
        }

        bool isImage = inspection.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (isImage && !BitMask.HasBit(featureMask, PlatformFeatures.ImageUploads)
            && !BitMask.HasBit(featureMask, PlatformFeatures.FileUploads))
        {
            throw new InvalidOperationException("You do not have permission to upload images.");
        }

        if (!isImage && !BitMask.HasBit(featureMask, PlatformFeatures.FileUploads))
            throw new InvalidOperationException("You do not have permission to upload files.");

        await using (Stream scanStream = file.OpenReadStream())
        {
            MalwareScanResult scanResult = await malwareScanner.ScanAsync(scanStream, ct);
            string? rejectionMessage = scanResult switch
            {
                MalwareScanResult.Infected => "Malware detected.",
                MalwareScanResult.Unavailable => "Virus scanner unavailable.",
                _ => null,
            };
            if (rejectionMessage is not null)
                throw new InvalidOperationException(rejectionMessage);
        }

        Directory.CreateDirectory(opts.RootPath);
        Guid attachmentId = Guid.NewGuid();
        string safeName = Path.GetFileName(file.FileName);
        string storageName = $"{attachmentId:N}_{safeName}";
        string fullPath = Path.Combine(opts.RootPath, storageName);

        await using (Stream saveStream = file.OpenReadStream())
        await using (FileStream fs = File.Create(fullPath))
        {
            await saveStream.CopyToAsync(fs, ct);
        }

        ChatAttachment attachment = new()
        {
            AttachmentId = attachmentId,
            UploadedByUserId = userId,
            OriginalFileName = safeName,
            ContentType = inspection.ContentType,
            SizeBytes = file.Length,
            StoragePath = storageName,
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = scope.AccountClass,
            TenantDatabaseName = scope.TenantDatabaseName,
            IsHazard = inspection.IsHazard,
            InlinePreviewKind = inspection.InlinePreviewKind,
        };
        db.ChatAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return new ChatAttachmentDto(
            attachmentId,
            safeName,
            inspection.ContentType,
            file.Length,
            accessTokenService.MintDownloadUrl(attachmentId, userId),
            inspection.IsHazard,
            inspection.InlinePreviewKind);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default,
        bool accessTokenValidated = false)
    {
        ChatAttachment? attachment = await db.ChatAttachments.AsNoTracking()
            .Include(a => a.MessageLinks)
            .ThenInclude(link => link.Message)
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
        if (attachment is null)
            return null;

        if (!accessTokenValidated && !await CanDownloadAsync(attachment, userId, ct))
            return null;

        UploadOptions opts = options.Value;
        string fullPath = Path.Combine(opts.RootPath, attachment.StoragePath);
        if (!File.Exists(fullPath))
            return null;

        Stream stream = File.OpenRead(fullPath);
        return (stream, attachment.ContentType, attachment.OriginalFileName);
    }

    public async Task<bool> DeleteUnattachedAsync(Guid attachmentId, Guid userId, CancellationToken ct = default)
    {
        ChatAttachment? attachment = await db.ChatAttachments
            .Include(a => a.MessageLinks)
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
        if (attachment is null)
            return false;

        if (attachment.UploadedByUserId != userId)
            throw new InvalidOperationException("You can only delete files you uploaded.");

        if (attachment.MessageLinks.Count > 0)
            throw new InvalidOperationException("Cannot delete an attachment linked to a message.");

        UploadOptions opts = options.Value;
        string fullPath = Path.Combine(opts.RootPath, attachment.StoragePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.ChatAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> CanDownloadAsync(ChatAttachment attachment, Guid userId, CancellationToken ct)
    {
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
