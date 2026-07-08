using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260708120000_AddRoleAppearance")]
public class AddRoleAppearance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsMentionableByUsers",
            table: "Roles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "MessageColor",
            table: "Roles",
            type: "character varying(7)",
            maxLength: 7,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SenderMessageColor",
            table: "ChatMessages",
            type: "character varying(7)",
            maxLength: 7,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SenderMessageColor", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "MessageColor", table: "Roles");
        migrationBuilder.DropColumn(name: "IsMentionableByUsers", table: "Roles");
    }
}
