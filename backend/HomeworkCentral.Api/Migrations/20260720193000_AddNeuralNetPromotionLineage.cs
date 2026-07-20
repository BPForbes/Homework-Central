using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

public partial class AddNeuralNetPromotionLineage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "NeuralNetCanonicalCheckpoints", columns: table => new
        {
            Generation = table.Column<long>(nullable: false), ModelVersion = table.Column<string>(maxLength: 64, nullable: false),
            ParametersBase64 = table.Column<string>(nullable: false), Checksum = table.Column<string>(maxLength: 64, nullable: false), CreatedAtUtc = table.Column<DateTime>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_NeuralNetCanonicalCheckpoints", x => x.Generation));
        migrationBuilder.CreateTable(name: "NeuralNetTrainingPromotions", columns: table => new
        {
            PromotionId = table.Column<Guid>(nullable: false, defaultValueSql: "gen_random_uuid()"), SessionId = table.Column<Guid>(nullable: false), PromotionSequence = table.Column<long>(nullable: false),
            Status = table.Column<string>(maxLength: 32, nullable: false), AttemptCount = table.Column<int>(nullable: false), LeaseId = table.Column<Guid>(nullable: true), LeaseExpiresAtUtc = table.Column<DateTime>(nullable: true),
            FailureReason = table.Column<string>(maxLength: 1000, nullable: true), PromotedGeneration = table.Column<long>(nullable: true), PromotionReportJson = table.Column<string>(nullable: true), CreatedAtUtc = table.Column<DateTime>(nullable: false), CompletedAtUtc = table.Column<DateTime>(nullable: true)
        }, constraints: table => table.PrimaryKey("PK_NeuralNetTrainingPromotions", x => x.PromotionId));
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_PromotionSequence", table: "NeuralNetTrainingPromotions", column: "PromotionSequence", unique: true);
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_SessionId", table: "NeuralNetTrainingPromotions", column: "SessionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_NeuralNetTrainingPromotions_Status_LeaseExpiresAtUtc", table: "NeuralNetTrainingPromotions", columns: new[] { "Status", "LeaseExpiresAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("NeuralNetTrainingPromotions"); migrationBuilder.DropTable("NeuralNetCanonicalCheckpoints"); }
}
