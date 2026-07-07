using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260707180000_AddCustomChannelsAndCustomRoles")]
    public class AddCustomChannelsAndCustomRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCustom",
                table: "Roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "ClaimHostRoomId",
                table: "Roles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomChannels",
                columns: table => new
                {
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CategoryKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RoomType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    InfoContent = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TieType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TieSubjectMask = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TieSubjectBitIndex = table.Column<short>(type: "smallint", nullable: true),
                    TiePlatformRoleBit = table.Column<short>(type: "smallint", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomChannels", x => x.ChannelId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomChannels_RoomId",
                table: "CustomChannels",
                column: "RoomId",
                unique: true);

            migrationBuilder.CreateTable(
                name: "CustomChannelAccessRules",
                columns: table => new
                {
                    AccessRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlatformRoleBit = table.Column<short>(type: "smallint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomChannelAccessRules", x => x.AccessRuleId);
                    table.ForeignKey(
                        name: "FK_CustomChannelAccessRules_CustomChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "CustomChannels",
                        principalColumn: "ChannelId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomChannelAccessRules_Roles_CustomRoleId",
                        column: x => x.CustomRoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomChannelAccessRules_ChannelId",
                table: "CustomChannelAccessRules",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomChannelAccessRules_CustomRoleId",
                table: "CustomChannelAccessRules",
                column: "CustomRoleId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomChannelAccessRules");
            migrationBuilder.DropTable(name: "CustomChannels");
            migrationBuilder.DropColumn(name: "IsCustom", table: "Roles");
            migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "Roles");
            migrationBuilder.DropColumn(name: "ClaimHostRoomId", table: "Roles");
        }
    }
}
