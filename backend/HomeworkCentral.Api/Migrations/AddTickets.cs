using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260716120000_AddTickets")]
public class AddTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AllowedUserId",
            table: "CustomChannelAccessRules",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TicketId",
            table: "ChatMentionNotifications",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TicketPayloadJson",
            table: "ChatMentionNotifications",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TicketPortalConfigs",
            columns: table => new
            {
                ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                CtaLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NextDisplayNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                TrackingMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TrackingInstructions = table.Column<string>(type: "text", nullable: true),
                DecisionLabelsJson = table.Column<string>(type: "text", nullable: false),
                MentionRoleRulesJson = table.Column<string>(type: "text", nullable: false),
                StaffAccessRulesJson = table.Column<string>(type: "text", nullable: false),
                IntakeSchemaJson = table.Column<string>(type: "text", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketPortalConfigs", x => x.ChannelId);
                table.ForeignKey(
                    name: "FK_TicketPortalConfigs_CustomChannels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "CustomChannels",
                    principalColumn: "ChannelId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Tickets",
            columns: table => new
            {
                TicketId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                PortalChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                ChatChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                RoomId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayNumber = table.Column<int>(type: "integer", nullable: false),
                Purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                OpenedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                IntakeAnswersJson = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tickets", x => x.TicketId);
                table.ForeignKey(
                    name: "FK_Tickets_CustomChannels_ChatChannelId",
                    column: x => x.ChatChannelId,
                    principalTable: "CustomChannels",
                    principalColumn: "ChannelId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Tickets_TicketPortalConfigs_PortalChannelId",
                    column: x => x.PortalChannelId,
                    principalTable: "TicketPortalConfigs",
                    principalColumn: "ChannelId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TicketUserWatches",
            columns: table => new
            {
                WatchId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                TrackedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ContextLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SetByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketUserWatches", x => x.WatchId);
                table.ForeignKey(
                    name: "FK_TicketUserWatches_Tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "Tickets",
                    principalColumn: "TicketId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tickets_ChatChannelId",
            table: "Tickets",
            column: "ChatChannelId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tickets_PortalChannelId_DisplayNumber",
            table: "Tickets",
            columns: new[] { "PortalChannelId", "DisplayNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tickets_RoomId",
            table: "Tickets",
            column: "RoomId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TicketUserWatches_TicketId",
            table: "TicketUserWatches",
            column: "TicketId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TicketUserWatches");
        migrationBuilder.DropTable(name: "Tickets");
        migrationBuilder.DropTable(name: "TicketPortalConfigs");

        migrationBuilder.DropColumn(name: "AllowedUserId", table: "CustomChannelAccessRules");
        migrationBuilder.DropColumn(name: "TicketId", table: "ChatMentionNotifications");
        migrationBuilder.DropColumn(name: "TicketPayloadJson", table: "ChatMentionNotifications");
    }
}
