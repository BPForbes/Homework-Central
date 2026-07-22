using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
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
    string? InlinePreviewKind,
    string ScanStatus);

public sealed record AttachmentReadResult(
    Stream? Stream,
    string? ContentType,
    string? FileName,
    MalwareScanResult ScanStatus,
    bool RequiresCaution);

public interface IChatAttachmentService
{
    Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default);
    Task<AttachmentReadResult?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default,
        bool accessTokenValidated = false,
        bool riskAcknowledged = false);
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
    IOptions<UploadOptions> options,
    IConfiguration configuration,
    IWebHostEnvironment environment) : IChatAttachmentService
{
    public async Task<ChatAttachmentDto> UploadAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    {
        UploadOptions uploadOptions = options.Value;
        if (file.Length <= 0 || file.Length > uploadOptions.MaxBytes)
            throw new InvalidOperationException($"File must be between 1 and {uploadOptions.MaxBytes} bytes.");

        EffectiveMaskDto masks = await effectiveMaskService.GetEffectiveMaskDtoAsync(userId, ct);
        System.Collections.BitArray featureMask = BitMask.FromBase64(masks.FeatureMask, 256);

        AccessScope? scope = accessScope.ResolveCurrent()
            ?? throw new InvalidOperationException("Access scope is required.");
        bool isDevelopmentPersona = (scope.AccountClass is AccountClass.DeveloperAccount or AccountClass.DevAdmin)
            && DevBypass.IsEnabled(configuration, environment);

        AttachmentTypeInspectionResult inspection;
        await using (Stream inspectStream = file.OpenReadStream())
        {
            inspection = typeInspector.Inspect(inspectStream, file.ContentType);
        }

        AssertUploadFeatureAllowed(inspection, featureMask, isDevelopmentPersona);

        MalwareScanResult scanStatus;
        await using (Stream scanStream = file.OpenReadStream())
            scanStatus = await malwareScanner.ScanAsync(scanStream, ct);

        Guid attachmentId = Guid.NewGuid();
        string safeName = Path.GetFileName(file.FileName);
        string storageName = await StoreAttachmentFileAsync(uploadOptions, file, attachmentId, safeName, ct);

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
            ScanStatus = scanStatus,
        };
        await PersistAttachmentMetadataAsync(attachment, ct);

        return new ChatAttachmentDto(
            attachmentId,
            safeName,
            inspection.ContentType,
            file.Length,
            accessTokenService.MintDownloadUrl(attachmentId, userId),
            inspection.IsHazard,
            inspection.InlinePreviewKind,
            scanStatus.ToString());
    }

    public async Task<AttachmentReadResult?> OpenReadAsync(
        Guid attachmentId,
        Guid userId,
        CancellationToken ct = default,
        bool accessTokenValidated = false,
        bool riskAcknowledged = false)
    {
        ChatAttachment? attachment = await db.ChatAttachments.AsNoTracking()
            .Include(a => a.MessageLinks)
            .ThenInclude(link => link.Message)
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
        if (attachment is null)
            return null;

        if (!accessTokenValidated && !await CanDownloadAsync(attachment, userId, ct))
            return null;

        bool requiresCaution = RequiresCautionGate(attachment.ScanStatus);
        if (requiresCaution && !riskAcknowledged)
        {
            return new AttachmentReadResult(
                null,
                null,
                null,
                attachment.ScanStatus,
                RequiresCaution: true);
        }

        UploadOptions uploadOptions = options.Value;
        string fullPath = Path.Combine(uploadOptions.RootPath, attachment.StoragePath);
        if (!File.Exists(fullPath))
            return null;

        Stream stream = File.OpenRead(fullPath);
        return new AttachmentReadResult(
            stream,
            attachment.ContentType,
            attachment.OriginalFileName,
            attachment.ScanStatus,
            RequiresCaution: false);
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

        UploadOptions uploadOptions = options.Value;
        string fullPath = Path.Combine(uploadOptions.RootPath, attachment.StoragePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        db.ChatAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void AssertUploadFeatureAllowed(
        AttachmentTypeInspectionResult inspection,
        System.Collections.BitArray featureMask,
        bool isDevelopmentPersona)
    {
        if (isDevelopmentPersona)
            return;

        switch (inspection.ContentType)
        {
            case { Length: > 0 } contentType
                when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                if (!CanUploadImage(featureMask))
                    throw new InvalidOperationException("You do not have permission to upload images.");
                break;

            default:
                if (!BitMask.HasBit(featureMask, PlatformFeatures.FileUploads))
                    throw new InvalidOperationException("You do not have permission to upload files.");
                break;
        }
    }

    private static bool CanUploadImage(System.Collections.BitArray featureMask) =>
        BitMask.HasBit(featureMask, PlatformFeatures.ImageUploads)
        || BitMask.HasBit(featureMask, PlatformFeatures.FileUploads);

    private static async Task<string> StoreAttachmentFileAsync(
        UploadOptions uploadOptions,
        IFormFile file,
        Guid attachmentId,
        string safeName,
        CancellationToken ct)
    {
        Directory.CreateDirectory(uploadOptions.RootPath);
        string storageName = $"{attachmentId:N}_{safeName}";
        string fullPath = Path.Combine(uploadOptions.RootPath, storageName);

        await using (Stream saveStream = file.OpenReadStream())
        await using (FileStream fileStream = File.Create(fullPath))
        {
            await saveStream.CopyToAsync(fileStream, ct);
        }

        return storageName;
    }

    private async Task PersistAttachmentMetadataAsync(ChatAttachment attachment, CancellationToken ct)
    {
        db.ChatAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
    }

    private static bool RequiresCautionGate(MalwareScanResult scanStatus)
    {
        // Only confirmed malware prompts the caution gate. Scanner downtime and NotScanned
        // fail open for ordinary downloads; see
        // docs/chat.md#download-authorization-and-caution-gate.
        return scanStatus is MalwareScanResult.Infected;
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
