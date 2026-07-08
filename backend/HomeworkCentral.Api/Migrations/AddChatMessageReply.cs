using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260708040000_AddChatMessageReply")]
public class AddChatMessageReply : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ReplyToMessageId",
            table: "ChatMessages",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ReplyToSenderId",
            table: "ChatMessages",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReplyToSenderUsername",
            table: "ChatMessages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReplyToContentSnippet",
            table: "ChatMessages",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_ReplyToMessageId",
            table: "ChatMessages",
            column: "ReplyToMessageId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ChatMessages_ReplyToMessageId",
            table: "ChatMessages");

        migrationBuilder.DropColumn(name: "ReplyToContentSnippet", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "ReplyToSenderUsername", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "ReplyToSenderId", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "ReplyToMessageId", table: "ChatMessages");
    }
}
