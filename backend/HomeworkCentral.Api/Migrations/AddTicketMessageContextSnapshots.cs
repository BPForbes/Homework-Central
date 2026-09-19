using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720140100_AddTicketMessageContextSnapshots")]
public class AddTicketMessageContextSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ContextSnapshot", "TicketMessageScores", maxLength: 5000, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("ContextSnapshot", "TicketModelTrainingExamples", maxLength: 5000, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ContextSnapshot", "TicketMessageScores");
        migrationBuilder.DropColumn("ContextSnapshot", "TicketModelTrainingExamples");
    }
}
