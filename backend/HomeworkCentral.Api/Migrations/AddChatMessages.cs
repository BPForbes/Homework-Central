using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260630120000_AddChatMessages")]
    public class AddChatMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RawContent = table.Column<string>(type: "text", nullable: false),
                    SanitizedContent = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OwnerAccountClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantDatabaseName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_RoomId_CreatedAtUtc",
                table: "ChatMessages",
                columns: new[] { "RoomId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SenderId",
                table: "ChatMessages",
                column: "SenderId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChatMessages");
        }
    }
}
