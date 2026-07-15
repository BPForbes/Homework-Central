using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260707200000_AddCustomRoleIconName")]
public class AddCustomRoleIconName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IconName",
            table: "Roles",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IconName", table: "Roles");
    }
}
