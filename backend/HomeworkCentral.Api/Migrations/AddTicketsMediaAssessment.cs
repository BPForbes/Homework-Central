using HomeworkCentral.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeworkCentral.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260716210000_AddTicketsMediaAssessment")]
public class AddTicketsMediaAssessment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FilterName",
            table: "TicketPortalConfigs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "General");

        migrationBuilder.Sql("""UPDATE "TicketPortalConfigs" SET "FilterName" = "Purpose" WHERE "FilterName" = 'General';""");

        migrationBuilder.AddColumn<string>(
            name: "FilterName",
            table: "Tickets",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "General");

        migrationBuilder.Sql("""UPDATE "Tickets" SET "FilterName" = "Purpose" WHERE "FilterName" = 'General';""");

        migrationBuilder.AddColumn<bool>(
            name: "AiTrackingOptOut",
            table: "Tickets",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "TrackingTemplateJson",
            table: "Tickets",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ApprovedDecision",
            table: "Tickets",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DecisionApprovedAtUtc",
            table: "Tickets",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DecisionApprovedByUserId",
            table: "Tickets",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ForwardedFromJson",
            table: "ChatMessages",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ChatAttachments",
            columns: table => new
            {
                AttachmentId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                OriginalFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OwnerAccountClass = table.Column<int>(type: "integer", nullable: false),
                TenantDatabaseName = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_ChatAttachments", x => x.AttachmentId));

        migrationBuilder.CreateTable(
            name: "ChatMessageAttachments",
            columns: table => new
            {
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                AttachmentId = table.Column<Guid>(type: "uuid", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessageAttachments", x => new { x.MessageId, x.AttachmentId });
                table.ForeignKey(
                    name: "FK_ChatMessageAttachments_ChatAttachments_AttachmentId",
                    column: x => x.AttachmentId,
                    principalTable: "ChatAttachments",
                    principalColumn: "AttachmentId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ChatMessageAttachments_ChatMessages_MessageId",
                    column: x => x.MessageId,
                    principalTable: "ChatMessages",
                    principalColumn: "MessageId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChatMessageVotes",
            columns: table => new
            {
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Value = table.Column<short>(type: "smallint", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessageVotes", x => new { x.MessageId, x.UserId });
                table.ForeignKey(
                    name: "FK_ChatMessageVotes_ChatMessages_MessageId",
                    column: x => x.MessageId,
                    principalTable: "ChatMessages",
                    principalColumn: "MessageId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChatLinkPreviews",
            columns: table => new
            {
                PreviewId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Description = table.Column<string>(type: "text", nullable: true),
                ImageUrl = table.Column<string>(type: "text", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatLinkPreviews", x => x.PreviewId);
                table.ForeignKey(
                    name: "FK_ChatLinkPreviews_ChatMessages_MessageId",
                    column: x => x.MessageId,
                    principalTable: "ChatMessages",
                    principalColumn: "MessageId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CandidateApplications",
            columns: table => new
            {
                CandidateApplicationId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PositionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                AiOptOut = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CandidateApplications", x => x.CandidateApplicationId);
                table.ForeignKey(
                    name: "FK_CandidateApplications_Tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "Tickets",
                    principalColumn: "TicketId",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "AssessmentEvents",
            columns: table => new
            {
                AssessmentEventId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                CandidateApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                RubricVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                EvaluatorModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RawEvaluationJson = table.Column<string>(type: "text", nullable: false),
                LlmScore = table.Column<double>(type: "double precision", nullable: false),
                CommunityScore = table.Column<double>(type: "double precision", nullable: true),
                CombinedScore = table.Column<double>(type: "double precision", nullable: false),
                EvidenceWeight = table.Column<double>(type: "double precision", nullable: false),
                Difficulty = table.Column<double>(type: "double precision", nullable: false),
                Relevance = table.Column<double>(type: "double precision", nullable: false),
                Confidence = table.Column<double>(type: "double precision", nullable: false),
                AuthenticityConfidence = table.Column<double>(type: "double precision", nullable: false),
                IsAdjustment = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssessmentEvents", x => x.AssessmentEventId);
                table.ForeignKey(
                    name: "FK_AssessmentEvents_CandidateApplications_CandidateApplicationId",
                    column: x => x.CandidateApplicationId,
                    principalTable: "CandidateApplications",
                    principalColumn: "CandidateApplicationId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AssessmentCompetencyEvidence",
            columns: table => new
            {
                AssessmentEventId = table.Column<Guid>(type: "uuid", nullable: false),
                CompetencyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MembershipWeight = table.Column<double>(type: "double precision", nullable: false),
                EffectiveEvidenceWeight = table.Column<double>(type: "double precision", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssessmentCompetencyEvidence", x => new { x.AssessmentEventId, x.CompetencyId });
                table.ForeignKey(
                    name: "FK_AssessmentCompetencyEvidence_AssessmentEvents_AssessmentEventId",
                    column: x => x.AssessmentEventId,
                    principalTable: "AssessmentEvents",
                    principalColumn: "AssessmentEventId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CandidateCompetencyStates",
            columns: table => new
            {
                CandidateApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                CompetencyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Alpha = table.Column<double>(type: "double precision", nullable: false),
                Beta = table.Column<double>(type: "double precision", nullable: false),
                MeanScore = table.Column<double>(type: "double precision", nullable: false),
                EvidenceVolume = table.Column<double>(type: "double precision", nullable: false),
                LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CandidateCompetencyStates", x => new { x.CandidateApplicationId, x.CompetencyId });
                table.ForeignKey(
                    name: "FK_CandidateCompetencyStates_CandidateApplications_CandidateApplicationId",
                    column: x => x.CandidateApplicationId,
                    principalTable: "CandidateApplications",
                    principalColumn: "CandidateApplicationId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CandidateDecisions",
            columns: table => new
            {
                CandidateDecisionId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                CandidateApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                Decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TriggeredBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "text", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CandidateDecisions", x => x.CandidateDecisionId);
                table.ForeignKey(
                    name: "FK_CandidateDecisions_CandidateApplications_CandidateApplicationId",
                    column: x => x.CandidateApplicationId,
                    principalTable: "CandidateApplications",
                    principalColumn: "CandidateApplicationId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VectorDocuments",
            columns: table => new
            {
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Namespace = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PositionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CanonicalRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                MetadataJson = table.Column<string>(type: "text", nullable: false),
                ContentText = table.Column<string>(type: "text", nullable: false),
                EmbeddingJson = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_VectorDocuments", x => x.DocumentId));

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessageVotes_MessageId",
            table: "ChatMessageVotes",
            column: "MessageId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessageAttachments_AttachmentId",
            table: "ChatMessageAttachments",
            column: "AttachmentId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatLinkPreviews_MessageId",
            table: "ChatLinkPreviews",
            column: "MessageId");

        migrationBuilder.CreateIndex(
            name: "IX_CandidateApplications_UserId_PositionId_Status",
            table: "CandidateApplications",
            columns: new[] { "UserId", "PositionId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_CandidateApplications_TicketId",
            table: "CandidateApplications",
            column: "TicketId");

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentEvents_CandidateApplicationId",
            table: "AssessmentEvents",
            column: "CandidateApplicationId");

        migrationBuilder.CreateIndex(
            name: "IX_VectorDocuments_Namespace",
            table: "VectorDocuments",
            column: "Namespace");

        migrationBuilder.CreateIndex(
            name: "IX_VectorDocuments_CanonicalRecordId",
            table: "VectorDocuments",
            column: "CanonicalRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_TicketUserWatches_TrackedUserId_IsActive",
            table: "TicketUserWatches",
            columns: new[] { "TrackedUserId", "IsActive" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssessmentCompetencyEvidence");
        migrationBuilder.DropTable(name: "CandidateCompetencyStates");
        migrationBuilder.DropTable(name: "CandidateDecisions");
        migrationBuilder.DropTable(name: "ChatLinkPreviews");
        migrationBuilder.DropTable(name: "ChatMessageAttachments");
        migrationBuilder.DropTable(name: "ChatMessageVotes");
        migrationBuilder.DropTable(name: "VectorDocuments");
        migrationBuilder.DropTable(name: "AssessmentEvents");
        migrationBuilder.DropTable(name: "ChatAttachments");
        migrationBuilder.DropTable(name: "CandidateApplications");

        migrationBuilder.DropIndex(
            name: "IX_TicketUserWatches_TrackedUserId_IsActive",
            table: "TicketUserWatches");

        migrationBuilder.DropColumn(name: "FilterName", table: "TicketPortalConfigs");
        migrationBuilder.DropColumn(name: "FilterName", table: "Tickets");
        migrationBuilder.DropColumn(name: "AiTrackingOptOut", table: "Tickets");
        migrationBuilder.DropColumn(name: "TrackingTemplateJson", table: "Tickets");
        migrationBuilder.DropColumn(name: "ApprovedDecision", table: "Tickets");
        migrationBuilder.DropColumn(name: "DecisionApprovedAtUtc", table: "Tickets");
        migrationBuilder.DropColumn(name: "DecisionApprovedByUserId", table: "Tickets");
        migrationBuilder.DropColumn(name: "ForwardedFromJson", table: "ChatMessages");
    }
}
