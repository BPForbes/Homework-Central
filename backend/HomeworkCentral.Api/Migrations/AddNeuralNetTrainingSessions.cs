using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720140000_AddNeuralNetTrainingSessions")]
public class AddNeuralNetTrainingSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NeuralNetTrainingSessions",
            columns: table => new
            {
                SessionId = table.Column<Guid>(nullable: false, defaultValueSql: "gen_random_uuid()"),
                StartedByUserId = table.Column<Guid>(nullable: false),
                RequestedTicketCount = table.Column<int>(nullable: false),
                MaxPassesPerTicket = table.Column<int>(nullable: false),
                Status = table.Column<string>(maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                StartedAtUtc = table.Column<DateTime>(nullable: true),
                CompletedAtUtc = table.Column<DateTime>(nullable: true),
                FailureReason = table.Column<string>(maxLength: 1000, nullable: true),
                ReportJson = table.Column<string>(nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_NeuralNetTrainingSessions", x => x.SessionId));
        migrationBuilder.CreateIndex("IX_NeuralNetTrainingSessions_CreatedAtUtc", "NeuralNetTrainingSessions", "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("NeuralNetTrainingSessions");
}
