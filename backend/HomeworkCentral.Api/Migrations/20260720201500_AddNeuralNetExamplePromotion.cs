using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HomeworkCentral.Api.Migrations;
public partial class AddNeuralNetExamplePromotion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "CanonicalGenerationApplied", table: "TicketModelTrainingExamples", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_TicketModelTrainingExamples_CanonicalGenerationApplied", table: "TicketModelTrainingExamples", column: "CanonicalGenerationApplied");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_TicketModelTrainingExamples_CanonicalGenerationApplied", table: "TicketModelTrainingExamples");
        migrationBuilder.DropColumn(name: "CanonicalGenerationApplied", table: "TicketModelTrainingExamples");
    }
}
