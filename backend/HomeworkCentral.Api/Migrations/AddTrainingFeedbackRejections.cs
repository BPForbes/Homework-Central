using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260718220000_AddTrainingFeedbackRejections")]
public class AddTrainingFeedbackRejections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("TrainingRejectedAtUtc", "TicketMessageScores", nullable: true);
        migrationBuilder.AddColumn<Guid>("TrainingRejectedByUserId", "TicketMessageScores", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("TrainingRejectedAtUtc", "TicketMessageScores");
        migrationBuilder.DropColumn("TrainingRejectedByUserId", "TicketMessageScores");
    }
}
