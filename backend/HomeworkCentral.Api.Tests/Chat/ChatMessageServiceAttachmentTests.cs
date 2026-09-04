using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.DTOs;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Uploads;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Tests.Chat;

/// <summary>
/// Real-Postgres coverage for attachment linking on send: missing IDs must fail the
/// entire send so attachment-only placeholders are never persisted without files.
/// </summary>
[Collection(nameof(ChatPostgresTestCollection))]
public class ChatMessageServiceAttachmentTests : IAsyncLifetime
{
    private readonly string _connectionString = ChatMessageServiceTestSupport.ResolveConnectionString();
    private bool _databaseAvailable;
    private AppDbContext _db = null!;
    private readonly string _roomId = ChatRoomCatalog.GeneralRoom.Id;

    public async Task InitializeAsync()
    {
        _databaseAvailable = ChatMessageServiceTestSupport.CanConnect(_connectionString);
        if (!_databaseAvailable)
            return;

        _db = await ChatMessageServiceTestSupport.CreateMigratedDatabaseAsync(_connectionString);
    }

    public async Task DisposeAsync()
    {
        if (_databaseAvailable)
            await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Send_rejects_missing_attachment_id_without_persisting_message()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid userId = Guid.NewGuid();
        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(_db, userId, "alice");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendMessageAsync(
                _roomId,
                userId,
                content: string.Empty,
                attachmentIds: [Guid.NewGuid()]));

        Assert.Equal("One or more attachments could not be found.", error.Message);
        Assert.Equal(0, await _db.ChatMessages.CountAsync());
        Assert.Equal(0, await _db.ChatMessageAttachments.CountAsync());
    }

    [SkippableFact]
    public async Task Send_links_owned_attachment_on_success()
    {
        Skip.IfNot(_databaseAvailable, "Requires Postgres at TEST_CHAT_DATABASE_URL.");

        Guid userId = Guid.NewGuid();
        Guid attachmentId = Guid.NewGuid();
        _db.ChatAttachments.Add(new ChatAttachment
        {
            AttachmentId = attachmentId,
            UploadedByUserId = userId,
            OriginalFileName = "notes.txt",
            ContentType = "text/plain",
            SizeBytes = 12,
            StoragePath = $"{attachmentId:N}_notes.txt",
            CreatedAtUtc = DateTime.UtcNow,
            OwnerAccountClass = AccountClass.RealAccount,
            TenantDatabaseName = null,
            IsHazard = false,
            InlinePreviewKind = "text",
            ScanStatus = MalwareScanResult.Clean,
        });
        await _db.SaveChangesAsync();

        ChatMessageService service = ChatMessageServiceTestSupport.BuildService(_db, userId, "alice");
        ChatMessageDto? message = await service.SendMessageAsync(
            _roomId,
            userId,
            content: string.Empty,
            attachmentIds: [attachmentId]);

        Assert.NotNull(message);
        Assert.Equal("(attachment)", message!.Content);
        Assert.Single(message.Attachments);
        Assert.Equal(attachmentId, message.Attachments[0].AttachmentId);
        Assert.Equal(1, await _db.ChatMessageAttachments.CountAsync());
    }
}
