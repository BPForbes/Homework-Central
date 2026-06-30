using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(MasterDbContext))]
[Migration("20260630180000_MasterPersonaTenants")]
public class MasterPersonaTenants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Tenants_DeveloperEmail",
            table: "Tenants");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_Slug",
            table: "Tenants");

        migrationBuilder.AddColumn<string>(
            name: "ClusterSlug",
            table: "Tenants",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "PersonaEmail",
            table: "Tenants",
            type: "character varying(320)",
            maxLength: 320,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<string>(
            name: "Slug",
            table: "Tenants",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32);

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_ClusterSlug",
            table: "Tenants",
            column: "ClusterSlug");

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_ClusterSlug_Slug",
            table: "Tenants",
            columns: ["ClusterSlug", "Slug"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_DeveloperEmail",
            table: "Tenants",
            column: "DeveloperEmail");

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_PersonaEmail",
            table: "Tenants",
            column: "PersonaEmail",
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Tenants_PersonaEmail_Lower",
            table: "Tenants",
            sql: "\"PersonaEmail\" = lower(\"PersonaEmail\")");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Tenants_PersonaEmail_Lower",
            table: "Tenants");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_ClusterSlug",
            table: "Tenants");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_ClusterSlug_Slug",
            table: "Tenants");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_DeveloperEmail",
            table: "Tenants");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_PersonaEmail",
            table: "Tenants");

        migrationBuilder.DropColumn(
            name: "ClusterSlug",
            table: "Tenants");

        migrationBuilder.DropColumn(
            name: "PersonaEmail",
            table: "Tenants");

        migrationBuilder.AlterColumn<string>(
            name: "Slug",
            table: "Tenants",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_DeveloperEmail",
            table: "Tenants",
            column: "DeveloperEmail",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_Slug",
            table: "Tenants",
            column: "Slug",
            unique: true);
    }
}
