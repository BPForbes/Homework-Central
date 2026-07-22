using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260720140200_AddNeuralNetTrainingMode")]
public class AddNeuralNetTrainingMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Mode", table: "NeuralNetTrainingSessions", maxLength: 16, nullable: false, defaultValue: "Both");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Mode", table: "NeuralNetTrainingSessions");
}