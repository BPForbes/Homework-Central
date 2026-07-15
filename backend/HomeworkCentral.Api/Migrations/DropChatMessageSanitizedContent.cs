using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

/// <summary>
/// Chat message content is raw Markdown (with embedded LaTeX and, where used, inline HTML),
/// rendered and sanitized entirely client-side (markdown-it -> KaTeX -> DOMPurify). Running an
/// HTML sanitizer over that raw Markdown source before storage — which is what
/// <c>SanitizedContent</c> held — corrupted valid syntax (angle brackets in code samples,
/// comparison operators, etc.), which is why sent messages lost formatting that the compose-time
/// preview showed correctly. Drops the column; <c>RawContent</c> was always stored unmangled and
/// is now the only content column.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260710120000_DropChatMessageSanitizedContent")]
public class DropChatMessageSanitizedContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SanitizedContent", table: "ChatMessages");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SanitizedContent",
            table: "ChatMessages",
            type: "text",
            nullable: true);
    }
}
