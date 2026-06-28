using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260628120000_AddSubjectExpertiseMasks")]
    public class AddSubjectExpertiseMasks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSubjectExpertiseMasks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubjectExpertiseMasks", x => new { x.UserId, x.Category });
                    table.ForeignKey(
                        name: "FK_UserSubjectExpertiseMasks_UserEffectiveMasks_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEffectiveMasks",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                ALTER TABLE "UserSubjectExpertiseMasks"
                    ADD COLUMN "ExpertiseMask" bit(128) NOT NULL DEFAULT B'0'::bit(128);
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'UserEffectiveMasks' AND column_name = 'ScienceMask'
                    ) THEN
                        INSERT INTO "UserSubjectExpertiseMasks" ("UserId", "Category", "ExpertiseMask")
                        SELECT "UserId", 'Science', "ScienceMask" FROM "UserEffectiveMasks";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'UserEffectiveMasks' AND column_name = 'ComputerScienceMask'
                    ) THEN
                        INSERT INTO "UserSubjectExpertiseMasks" ("UserId", "Category", "ExpertiseMask")
                        SELECT "UserId", 'ComputerScience', "ComputerScienceMask" FROM "UserEffectiveMasks";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'UserEffectiveMasks' AND column_name = 'MathematicsMask'
                    ) THEN
                        INSERT INTO "UserSubjectExpertiseMasks" ("UserId", "Category", "ExpertiseMask")
                        SELECT "UserId", 'Mathematics', "MathematicsMask" FROM "UserEffectiveMasks";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'UserEffectiveMasks' AND column_name = 'LanguageMask'
                    ) THEN
                        INSERT INTO "UserSubjectExpertiseMasks" ("UserId", "Category", "ExpertiseMask")
                        SELECT "UserId", 'Languages', "LanguageMask" FROM "UserEffectiveMasks";
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "UserEffectiveMasks"
                    DROP COLUMN IF EXISTS "ScienceMask",
                    DROP COLUMN IF EXISTS "ComputerScienceMask",
                    DROP COLUMN IF EXISTS "MathematicsMask",
                    DROP COLUMN IF EXISTS "LanguageMask";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "UserEffectiveMasks"
                    ADD COLUMN IF NOT EXISTS "ScienceMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN IF NOT EXISTS "ComputerScienceMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN IF NOT EXISTS "MathematicsMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN IF NOT EXISTS "LanguageMask" bit(128) NOT NULL DEFAULT B'0'::bit(128);
                """);

            migrationBuilder.Sql("""
                UPDATE "UserEffectiveMasks" u
                SET "ScienceMask" = s."ExpertiseMask"
                FROM "UserSubjectExpertiseMasks" s
                WHERE u."UserId" = s."UserId" AND s."Category" = 'Science';

                UPDATE "UserEffectiveMasks" u
                SET "ComputerScienceMask" = s."ExpertiseMask"
                FROM "UserSubjectExpertiseMasks" s
                WHERE u."UserId" = s."UserId" AND s."Category" = 'ComputerScience';

                UPDATE "UserEffectiveMasks" u
                SET "MathematicsMask" = s."ExpertiseMask"
                FROM "UserSubjectExpertiseMasks" s
                WHERE u."UserId" = s."UserId" AND s."Category" = 'Mathematics';

                UPDATE "UserEffectiveMasks" u
                SET "LanguageMask" = s."ExpertiseMask"
                FROM "UserSubjectExpertiseMasks" s
                WHERE u."UserId" = s."UserId" AND s."Category" = 'Languages';
                """);

            migrationBuilder.DropTable(name: "UserSubjectExpertiseMasks");
        }
    }
}
