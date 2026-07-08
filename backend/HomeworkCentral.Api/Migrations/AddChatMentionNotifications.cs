using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260704120000_AddChatMentionNotifications")]
public class AddChatMentionNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChatMentionNotifications",
            columns: table => new
            {
                NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                SenderUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RoomId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RoomDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CategoryKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CategoryDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                MessageContent = table.Column<string>(type: "text", nullable: false),
                MentionKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                OwnerAccountClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TenantDatabaseName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMentionNotifications", x => x.NotificationId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChatMentionNotifications_RecipientUserId_ReadAtUtc",
            table: "ChatMentionNotifications",
            columns: new[] { "RecipientUserId", "ReadAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ChatMentionNotifications_RecipientUserId_CreatedAtUtc",
            table: "ChatMentionNotifications",
            columns: new[] { "RecipientUserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ChatMentionNotifications_RecipientUserId_CategoryKey",
            table: "ChatMentionNotifications",
            columns: new[] { "RecipientUserId", "CategoryKey" });

        migrationBuilder.CreateIndex(
            name: "IX_ChatMentionNotifications_MessageId",
            table: "ChatMentionNotifications",
            column: "MessageId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChatMentionNotifications");
    }
}
