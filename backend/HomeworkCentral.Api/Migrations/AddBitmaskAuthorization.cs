using System;
using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260627200000_AddBitmaskAuthorization")]
    public class AddBitmaskAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Roles"
                    ADD COLUMN "RoleMask" bit(64) NOT NULL DEFAULT B'0'::bit(64),
                    ADD COLUMN "FeatureMask" bit(256) NOT NULL DEFAULT B'0'::bit(256);
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Permissions"
                        WHERE "Category" IS NOT NULL
                          AND length("Category") > 64
                    ) THEN
                        RAISE EXCEPTION 'Permissions.Category contains values longer than 64 characters';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Permissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ParentSubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectMask = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BitIndex = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.SubjectId);
                    table.ForeignKey(
                        name: "FK_Subjects_Subjects_ParentSubjectId",
                        column: x => x.ParentSubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserEffectiveMasks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEffectiveMasks", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserEffectiveMasks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                ALTER TABLE "UserEffectiveMasks"
                    ADD COLUMN "EffectiveRoleMask" bit(64) NOT NULL DEFAULT B'0'::bit(64),
                    ADD COLUMN "EffectiveModerationMask" bit(256) NOT NULL DEFAULT B'0'::bit(256),
                    ADD COLUMN "EffectiveFeatureMask" bit(256) NOT NULL DEFAULT B'0'::bit(256),
                    ADD COLUMN "GeneralSubjectMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN "ComputerScienceMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN "MathematicsMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN "LanguageMask" bit(128) NOT NULL DEFAULT B'0'::bit(128),
                    ADD COLUMN "StatusMask" bit(64) NOT NULL DEFAULT B'0'::bit(64);
                """);

            migrationBuilder.CreateTable(
                name: "UserSubjects",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubjects", x => new { x.UserId, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_UserSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSubjects_Users_AssignedBy",
                        column: x => x.AssignedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserSubjects_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_ParentSubjectId",
                table: "Subjects",
                column: "ParentSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SubjectMask_BitIndex",
                table: "Subjects",
                columns: new[] { "SubjectMask", "BitIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubjects_AssignedBy",
                table: "UserSubjects",
                column: "AssignedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubjects_SubjectId",
                table: "UserSubjects",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserEffectiveMasks");

            migrationBuilder.DropTable(
                name: "UserSubjects");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.Sql("""
                ALTER TABLE "Roles"
                    DROP COLUMN "RoleMask",
                    DROP COLUMN "FeatureMask";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Permissions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
