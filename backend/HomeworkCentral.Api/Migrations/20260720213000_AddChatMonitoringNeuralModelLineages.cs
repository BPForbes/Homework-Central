using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720213000_AddChatMonitoringNeuralModelLineages")]
public partial class AddChatMonitoringNeuralModelLineages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ChatMonitoringKind", table: "TicketModelTrainingExamples", maxLength: 16, nullable: false, defaultValue: "Moderation");
        migrationBuilder.AddColumn<string>(name: "ChatMonitoringKind", table: "NeuralNetCanonicalCheckpoints", maxLength: 16, nullable: false, defaultValue: "Moderation");
        migrationBuilder.AddColumn<string>(name: "ArchitectureVersion", table: "NeuralNetCanonicalCheckpoints", maxLength: 64, nullable: false, defaultValue: "hc-student-mlp-v3-legacy");
        migrationBuilder.AddColumn<string>(name: "RuntimeKind", table: "NeuralNetCanonicalCheckpoints", maxLength: 32, nullable: false, defaultValue: "LegacyManual");
        migrationBuilder.AddColumn<string>(name: "ChatMonitoringKind", table: "NeuralNetTrainingPromotions", maxLength: 16, nullable: false, defaultValue: "Moderation");

        migrationBuilder.DropPrimaryKey(name: "PK_NeuralNetCanonicalCheckpoints", table: "NeuralNetCanonicalCheckpoints");
        migrationBuilder.AddPrimaryKey(name: "PK_NeuralNetCanonicalCheckpoints", table: "NeuralNetCanonicalCheckpoints", columns: new[] { "ChatMonitoringKind", "Generation" });
        migrationBuilder.DropIndex(name: "IX_NeuralNetTrainingPromotions_PromotionSequence", table: "NeuralNetTrainingPromotions");
        migrationBuilder.DropIndex(name: "IX_NeuralNetTrainingPromotions_SessionId", table: "NeuralNetTrainingPromotions");
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_ChatMonitoringKind_PromotionSequence", table: "NeuralNetTrainingPromotions", columns: new[] { "ChatMonitoringKind", "PromotionSequence" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_SessionId_ChatMonitoringKind", table: "NeuralNetTrainingPromotions", columns: new[] { "SessionId", "ChatMonitoringKind" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_TicketModelTrainingExamples_NeuralNetTrainingSessionId_ChatMonitoringKind", table: "TicketModelTrainingExamples", columns: new[] { "NeuralNetTrainingSessionId", "ChatMonitoringKind" });

        migrationBuilder.CreateTable(name: "ChatMonitoringNeuralModelRuns", columns: table => new
        {
            RunId = table.Column<Guid>(nullable: false, defaultValueSql: "gen_random_uuid()"),
            SessionId = table.Column<Guid>(nullable: false),
            ChatMonitoringKind = table.Column<string>(maxLength: 16, nullable: false),
            Status = table.Column<string>(maxLength: 32, nullable: false),
            WorkerReplayJson = table.Column<string>(nullable: true),
            PromotionReplayJson = table.Column<string>(nullable: true),
            CanonicalGeneration = table.Column<long>(nullable: true),
            CreatedAtUtc = table.Column<DateTime>(nullable: false),
            StartedAtUtc = table.Column<DateTime>(nullable: true),
            CompletedAtUtc = table.Column<DateTime>(nullable: true),
            FailureReason = table.Column<string>(maxLength: 1000, nullable: true)
        }, constraints: table => table.PrimaryKey("PK_ChatMonitoringNeuralModelRuns", x => x.RunId));
        migrationBuilder.CreateIndex(name: "IX_ChatMonitoringNeuralModelRuns_SessionId_ChatMonitoringKind", table: "ChatMonitoringNeuralModelRuns", columns: new[] { "SessionId", "ChatMonitoringKind" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ChatMonitoringNeuralModelRuns_Status_CreatedAtUtc", table: "ChatMonitoringNeuralModelRuns", columns: new[] { "Status", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChatMonitoringNeuralModelRuns");
        migrationBuilder.DropIndex(name: "IX_TicketModelTrainingExamples_NeuralNetTrainingSessionId_ChatMonitoringKind", table: "TicketModelTrainingExamples");
        migrationBuilder.DropIndex(name: "IX_NeuralNetTrainingPromotions_ChatMonitoringKind_PromotionSequence", table: "NeuralNetTrainingPromotions");
        migrationBuilder.DropIndex(name: "IX_NeuralNetTrainingPromotions_SessionId_ChatMonitoringKind", table: "NeuralNetTrainingPromotions");
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_PromotionSequence", table: "NeuralNetTrainingPromotions", column: "PromotionSequence", unique: true);
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_SessionId", table: "NeuralNetTrainingPromotions", column: "SessionId", unique: true);
        migrationBuilder.DropPrimaryKey(name: "PK_NeuralNetCanonicalCheckpoints", table: "NeuralNetCanonicalCheckpoints");
        migrationBuilder.AddPrimaryKey(name: "PK_NeuralNetCanonicalCheckpoints", table: "NeuralNetCanonicalCheckpoints", column: "Generation");
        migrationBuilder.DropColumn(name: "ChatMonitoringKind", table: "TicketModelTrainingExamples");
        migrationBuilder.DropColumn(name: "ChatMonitoringKind", table: "NeuralNetCanonicalCheckpoints");
        migrationBuilder.DropColumn(name: "ArchitectureVersion", table: "NeuralNetCanonicalCheckpoints");
        migrationBuilder.DropColumn(name: "RuntimeKind", table: "NeuralNetCanonicalCheckpoints");
        migrationBuilder.DropColumn(name: "ChatMonitoringKind", table: "NeuralNetTrainingPromotions");
    }
}
