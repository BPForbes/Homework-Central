using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260904170000_AddAITracking")]
public class AddAITracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AIModelLineages",
            columns: table => new
            {
                LineageId = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PortalChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AIModelLineages", x => x.LineageId);
                table.ForeignKey(
                    name: "FK_AIModelLineages_TicketPortalConfigs_PortalChannelId",
                    column: x => x.PortalChannelId,
                    principalTable: "TicketPortalConfigs",
                    principalColumn: "ChannelId",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "AICategories",
            columns: table => new
            {
                CategoryId = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                LineageId = table.Column<int>(type: "integer", nullable: false),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsCatchAll = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AICategories", x => x.CategoryId);
                table.ForeignKey(
                    name: "FK_AICategories_AIModelLineages_LineageId",
                    column: x => x.LineageId,
                    principalTable: "AIModelLineages",
                    principalColumn: "LineageId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AITrackingSessions",
            columns: table => new
            {
                SessionId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                LineageId = table.Column<int>(type: "integer", nullable: false),
                TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                MessageIndex = table.Column<int>(type: "integer", nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingSessions", x => x.SessionId);
                table.ForeignKey(
                    name: "FK_AITrackingSessions_AIModelLineages_LineageId",
                    column: x => x.LineageId,
                    principalTable: "AIModelLineages",
                    principalColumn: "LineageId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AITrackingSessions_Tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "Tickets",
                    principalColumn: "TicketId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AITrackingCategoryWeights",
            columns: table => new
            {
                CategoryWeightId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SessionId = table.Column<long>(type: "bigint", nullable: false),
                CategoryId = table.Column<int>(type: "integer", nullable: false),
                Weight = table.Column<double>(type: "double precision", nullable: false),
                IsHumanCorrected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                HumanOverrideCategoryId = table.Column<int>(type: "integer", nullable: true),
                HumanCorrectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                HumanCorrectionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingCategoryWeights", x => x.CategoryWeightId);
                table.ForeignKey(
                    name: "FK_AITrackingCategoryWeights_AITrackingSessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "AITrackingSessions",
                    principalColumn: "SessionId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AITrackingCategoryWeights_AICategories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "AICategories",
                    principalColumn: "CategoryId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AITrackingCategoryWeights_AICategories_HumanOverrideCategoryId",
                    column: x => x.HumanOverrideCategoryId,
                    principalTable: "AICategories",
                    principalColumn: "CategoryId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AITrackingPredictions",
            columns: table => new
            {
                PredictionId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SessionId = table.Column<long>(type: "bigint", nullable: false),
                PredictedCategoryId = table.Column<int>(type: "integer", nullable: false),
                PredictedScore = table.Column<float>(type: "real", nullable: false),
                ActualCategoryId = table.Column<int>(type: "integer", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingPredictions", x => x.PredictionId);
                table.ForeignKey(
                    name: "FK_AITrackingPredictions_AITrackingSessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "AITrackingSessions",
                    principalColumn: "SessionId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AITrackingPredictions_AICategories_PredictedCategoryId",
                    column: x => x.PredictedCategoryId,
                    principalTable: "AICategories",
                    principalColumn: "CategoryId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AITrackingPredictions_AICategories_ActualCategoryId",
                    column: x => x.ActualCategoryId,
                    principalTable: "AICategories",
                    principalColumn: "CategoryId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AIModelLineages_Slug",
            table: "AIModelLineages",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AIModelLineages_PortalChannelId",
            table: "AIModelLineages",
            column: "PortalChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_AICategories_LineageId_Slug",
            table: "AICategories",
            columns: new[] { "LineageId", "Slug" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingSessions_TicketId_MessageIndex",
            table: "AITrackingSessions",
            columns: new[] { "TicketId", "MessageIndex" });

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingSessions_LineageId_CreatedAtUtc",
            table: "AITrackingSessions",
            columns: new[] { "LineageId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingSessions_CreatedAtUtc",
            table: "AITrackingSessions",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingCategoryWeights_SessionId_CategoryId",
            table: "AITrackingCategoryWeights",
            columns: new[] { "SessionId", "CategoryId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingCategoryWeights_HumanOverrideCategoryId",
            table: "AITrackingCategoryWeights",
            column: "HumanOverrideCategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingPredictions_SessionId",
            table: "AITrackingPredictions",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingPredictions_PredictedCategoryId",
            table: "AITrackingPredictions",
            column: "PredictedCategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingPredictions_ActualCategoryId",
            table: "AITrackingPredictions",
            column: "ActualCategoryId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AITrackingPredictions");
        migrationBuilder.DropTable(name: "AITrackingCategoryWeights");
        migrationBuilder.DropTable(name: "AITrackingSessions");
        migrationBuilder.DropTable(name: "AICategories");
        migrationBuilder.DropTable(name: "AIModelLineages");
    }
}
