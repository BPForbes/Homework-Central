using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260627213000_AddAuthorizationCheckConstraints")]
    public class AddAuthorizationCheckConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Subjects_BitIndex_Range",
                table: "Subjects",
                sql: "\"BitIndex\" BETWEEN 0 AND 127");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subjects_SubjectMask_Allowed",
                table: "Subjects",
                sql: """
                    "SubjectMask" IN (
                        'General', 'Mathematics', 'Science', 'ComputerScience', 'Languages',
                        'History', 'Business', 'Art', 'Music', 'Engineering', 'Medicine',
                        'Finance', 'Economics', 'Education'
                    )
                    """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Subjects_SubjectMask_Allowed",
                table: "Subjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Subjects_BitIndex_Range",
                table: "Subjects");
        }
    }
}
