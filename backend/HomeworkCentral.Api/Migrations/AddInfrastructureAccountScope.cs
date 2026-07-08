using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260707190000_AddInfrastructureAccountScope")]
public class AddInfrastructureAccountScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OwnerAccountClass",
            table: "CustomChannels",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: AccountClass.RealAccount.ToString());

        migrationBuilder.AddColumn<string>(
            name: "OwnerAccountClass",
            table: "Roles",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: AccountClass.RealAccount.ToString());
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "OwnerAccountClass", table: "CustomChannels");
        migrationBuilder.DropColumn(name: "OwnerAccountClass", table: "Roles");
    }
}
