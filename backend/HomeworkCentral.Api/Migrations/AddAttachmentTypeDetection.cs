using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260717130000_AddAttachmentTypeDetection")]
public class AddAttachmentTypeDetection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsHazard",
            table: "ChatAttachments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "InlinePreviewKind",
            table: "ChatAttachments",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "InlinePreviewKind", table: "ChatAttachments");
        migrationBuilder.DropColumn(name: "IsHazard", table: "ChatAttachments");
    }
}
