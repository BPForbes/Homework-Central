using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    /// <summary>
    /// Chat messages always live in the master database (see <see cref="Models.ChatMessage"/>),
    /// but a sender's User row often lives only in their own tenant database, so a same-database
    /// foreign key on SenderId rejects every message sent by a dev persona account. The index is
    /// kept for query performance; only the FK constraint is dropped.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260701030000_DropChatMessagesSenderForeignKey")]
    public class DropChatMessagesSenderForeignKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_SenderId",
                table: "ChatMessages");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_SenderId",
                table: "ChatMessages",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
