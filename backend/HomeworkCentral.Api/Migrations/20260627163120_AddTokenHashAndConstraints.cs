using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenHashAndConstraints : Migration
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
