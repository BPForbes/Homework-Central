using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260717143000_AddAttachmentScanStatus")]
public class AddAttachmentScanStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ScanStatus",
            table: "ChatAttachments",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Unknown");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ScanStatus", table: "ChatAttachments");
    }
}
