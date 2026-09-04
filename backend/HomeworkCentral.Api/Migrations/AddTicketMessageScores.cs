using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260718190000_AddTicketMessageScores")]
public class AddTicketMessageScores : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TicketMessageScores",
            columns: table => new
            {
                ScoreEventId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                TrackedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousScore = table.Column<double>(type: "double precision", nullable: false),
                ScoreDelta = table.Column<double>(type: "double precision", nullable: false),
                CurrentScore = table.Column<double>(type: "double precision", nullable: false),
                EvidenceConfidence = table.Column<double>(type: "double precision", nullable: false),
                Relevance = table.Column<double>(type: "double precision", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                EvaluatorModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RawEvaluationJson = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketMessageScores", x => x.ScoreEventId);
                table.ForeignKey(
                    name: "FK_TicketMessageScores_Tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "Tickets",
                    principalColumn: "TicketId",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    "CK_TicketMessageScores_PreviousScore",
                    "\"PreviousScore\" >= 0 AND \"PreviousScore\" <= 1");
                table.CheckConstraint(
                    "CK_TicketMessageScores_ScoreDelta",
                    "\"ScoreDelta\" >= -1 AND \"ScoreDelta\" <= 1");
                table.CheckConstraint(
                    "CK_TicketMessageScores_CurrentScore",
                    "\"CurrentScore\" >= 0 AND \"CurrentScore\" <= 1");
                table.CheckConstraint(
                    "CK_TicketMessageScores_EvidenceConfidence",
                    "\"EvidenceConfidence\" >= 0 AND \"EvidenceConfidence\" <= 1");
                table.CheckConstraint(
                    "CK_TicketMessageScores_Relevance",
                    "\"Relevance\" >= 0 AND \"Relevance\" <= 1");
            });

        migrationBuilder.CreateIndex(
            name: "IX_TicketMessageScores_TicketId_MessageId",
            table: "TicketMessageScores",
            columns: new[] { "TicketId", "MessageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TicketMessageScores_TicketId_TrackedUserId_CreatedAtUtc",
            table: "TicketMessageScores",
            columns: new[] { "TicketId", "TrackedUserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TicketMessageScores");
    }
}
