using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260709180000_AddInfoEntries")]
public class AddInfoEntries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InfoEntries",
            columns: table => new
            {
                EntryId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InfoEntries", x => x.EntryId);
                table.ForeignKey(
                    name: "FK_InfoEntries_CustomChannels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "CustomChannels",
                    principalColumn: "ChannelId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InfoEntries_ChannelId",
            table: "InfoEntries",
            column: "ChannelId");

        // Carry existing single-blob info content forward as each room's first entry so nothing
        // authored before this migration disappears from the new feed view.
        migrationBuilder.Sql(
            """
            INSERT INTO "InfoEntries" ("EntryId", "ChannelId", "AuthorUserId", "AuthorUsername", "Content", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT gen_random_uuid(), c."ChannelId", c."CreatedByUserId", COALESCE(u."Username", 'Unknown'), c."InfoContent", c."CreatedAtUtc", c."UpdatedAtUtc"
            FROM "CustomChannels" c
            LEFT JOIN "Users" u ON u."UserId" = c."CreatedByUserId"
            WHERE c."RoomType" = 'Info' AND c."InfoContent" IS NOT NULL AND btrim(c."InfoContent") <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InfoEntries");
    }
}
