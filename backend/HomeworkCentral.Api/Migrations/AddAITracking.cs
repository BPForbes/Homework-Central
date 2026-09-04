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
            name: "AITrackingSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageIndex = table.Column<int>(type: "integer", nullable: false),
                NeuralModelKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingSessions", x => x.Id);
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
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TrackingSessionId = table.Column<long>(type: "bigint", nullable: false),
                CategoryName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Weight = table.Column<double>(type: "double precision", nullable: false),
                IsHumanCorrected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                HumanCategoryOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                HumanCorrectionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingCategoryWeights", x => x.Id);
                table.ForeignKey(
                    name: "FK_AITrackingCategoryWeights_AITrackingSessions_TrackingSessionId",
                    column: x => x.TrackingSessionId,
                    principalTable: "AITrackingSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AITrackingPredictions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TrackingSessionId = table.Column<long>(type: "bigint", nullable: false),
                PredictedCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PredictedScore = table.Column<float>(type: "real", nullable: false),
                ActualOutcome = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AITrackingPredictions", x => x.Id);
                table.ForeignKey(
                    name: "FK_AITrackingPredictions_AITrackingSessions_TrackingSessionId",
                    column: x => x.TrackingSessionId,
                    principalTable: "AITrackingSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingSessions_TicketId_MessageIndex",
            table: "AITrackingSessions",
            columns: new[] { "TicketId", "MessageIndex" });

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingSessions_CreatedAtUtc",
            table: "AITrackingSessions",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingCategoryWeights_TrackingSessionId_CategoryName",
            table: "AITrackingCategoryWeights",
            columns: new[] { "TrackingSessionId", "CategoryName" });

        migrationBuilder.CreateIndex(
            name: "IX_AITrackingPredictions_TrackingSessionId",
            table: "AITrackingPredictions",
            column: "TrackingSessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AITrackingPredictions");
        migrationBuilder.DropTable(name: "AITrackingCategoryWeights");
        migrationBuilder.DropTable(name: "AITrackingSessions");
    }
}
