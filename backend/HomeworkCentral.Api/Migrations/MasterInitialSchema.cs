using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(MasterDbContext))]
[Migration("20260630120000_MasterInitialSchema")]
public class MasterInitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tenants",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                DatabaseName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Slug = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DeveloperEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                ClusterEnvironment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tenants", x => x.TenantId);
                table.CheckConstraint("CK_Tenants_DeveloperEmail_Lower", "\"DeveloperEmail\" = lower(\"DeveloperEmail\")");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_DatabaseName",
            table: "Tenants",
            column: "DatabaseName",
            unique: true);

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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Tenants");
    }
}
