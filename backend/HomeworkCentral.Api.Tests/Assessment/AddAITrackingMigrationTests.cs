using HomeworkCentral.Api.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace HomeworkCentral.Api.Tests.Assessment;

/// <summary>
/// Hand-authored AddAITracking must stay on the Npgsql provider and name Tickets.TicketId.
/// A SQL Server identity enum in the value-generation annotation fails Release and CodeQL
/// builds because the API project does not reference Microsoft.EntityFrameworkCore.SqlServer.
/// </summary>
public class AddAITrackingMigrationTests
{
    [Fact]
    public void Identity_columns_use_npgsql_identity_by_default()
    {
        List<AddColumnOperation> identityColumns = new AddAITracking().UpOperations
            .OfType<CreateTableOperation>()
            .SelectMany(table => table.Columns)
            .Where(column => !column.IsNullable && HasIdentityAnnotation(column))
            .ToList();

        Assert.True(identityColumns.Count >= 5);
        foreach (AddColumnOperation column in identityColumns)
        {
            IAnnotation? annotation = column.FindAnnotation("Npgsql:ValueGenerationStrategy");
            Assert.NotNull(annotation);
            Assert.Equal(NpgsqlValueGenerationStrategy.IdentityByDefaultColumn, annotation.Value);
        }
    }

    [Fact]
    public void Ticket_foreign_key_targets_ticket_id()
    {
        CreateTableOperation sessions = RequireTable("AITrackingSessions");
        AddForeignKeyOperation ticketForeignKey = sessions.ForeignKeys.Single(
            key => key.PrincipalTable == "Tickets");

        Assert.NotNull(ticketForeignKey.PrincipalColumns);
        Assert.NotNull(ticketForeignKey.Columns);
        Assert.Equal(new[] { "TicketId" }, ticketForeignKey.PrincipalColumns);
        Assert.Equal(new[] { "TicketId" }, ticketForeignKey.Columns);
    }

    [Fact]
    public void Schema_has_lookup_junction_and_entity_tables()
    {
        HashSet<string> tables = new AddAITracking().UpOperations
            .OfType<CreateTableOperation>()
            .Select(table => table.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("AIModelLineages", tables);
        Assert.Contains("AICategories", tables);
        Assert.Contains("AITrackingSessions", tables);
        Assert.Contains("AITrackingCategoryWeights", tables);
        Assert.Contains("AITrackingPredictions", tables);
    }

    [Fact]
    public void Operations_do_not_reference_sql_server_types()
    {
        IEnumerable<IAnnotation> annotations = new AddAITracking().UpOperations
            .OfType<CreateTableOperation>()
            .SelectMany(table => table.Columns)
            .SelectMany(column => column.GetAnnotations());

        foreach (IAnnotation annotation in annotations)
        {
            string valueTypeName = annotation.Value?.GetType().FullName ?? string.Empty;
            Assert.DoesNotContain("SqlServer", valueTypeName, StringComparison.Ordinal);
        }
    }

    private static bool HasIdentityAnnotation(AddColumnOperation column) =>
        column.FindAnnotation("Npgsql:ValueGenerationStrategy") is not null;

    private static CreateTableOperation RequireTable(string tableName)
    {
        CreateTableOperation? table = new AddAITracking().UpOperations
            .OfType<CreateTableOperation>()
            .SingleOrDefault(candidate => candidate.Name == tableName);

        Assert.NotNull(table);
        return table;
    }
}
