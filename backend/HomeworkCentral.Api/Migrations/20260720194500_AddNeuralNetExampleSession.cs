using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HomeworkCentral.Api.Migrations;
public partial class AddNeuralNetExampleSession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "NeuralNetTrainingSessionId", table: "TicketModelTrainingExamples", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_TicketModelTrainingExamples_NeuralNetTrainingSessionId", table: "TicketModelTrainingExamples", column: "NeuralNetTrainingSessionId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_TicketModelTrainingExamples_NeuralNetTrainingSessionId", table: "TicketModelTrainingExamples");
        migrationBuilder.DropColumn(name: "NeuralNetTrainingSessionId", table: "TicketModelTrainingExamples");
    }
}
