using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260627163120_AddTokenHashAndConstraints")]
    public class AddTokenHashAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Token",
                table: "RefreshTokens",
                newName: "TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_TokenHash");

            migrationBuilder.AlterColumn<short>(
                name: "PermissionId",
                table: "Permissions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT lower("Email")
                        FROM "Users"
                        GROUP BY lower("Email")
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot normalize emails: case-insensitive duplicate addresses exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "Email" = lower("Email")
                WHERE "Email" <> lower("Email");
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Email_Lower",
                table: "Users",
                sql: "\"Email\" = lower(\"Email\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Permissions_PermissionId_Range",
                table: "Permissions",
                sql: "\"PermissionId\" BETWEEN 0 AND 255");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Email_Lower",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Permissions_PermissionId_Range",
                table: "Permissions");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "RefreshTokens",
                newName: "Token");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_Token");

            migrationBuilder.AlterColumn<short>(
                name: "PermissionId",
                table: "Permissions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
