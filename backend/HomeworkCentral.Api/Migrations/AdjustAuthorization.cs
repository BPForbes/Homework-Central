using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260628100000_AdjustAuthorization")]
    public class AdjustAuthorization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "UserEffectiveMasks"
                    ADD COLUMN IF NOT EXISTS "ScienceMask" bit(128) NOT NULL DEFAULT B'0'::bit(128);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "UserEffectiveMasks"
                    DROP COLUMN IF EXISTS "ScienceMask";
                """);
        }
    }
}
