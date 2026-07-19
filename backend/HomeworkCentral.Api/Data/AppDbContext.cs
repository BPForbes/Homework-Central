using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public partial class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IAccessScopeAccessor? accessScopeAccessor = null) : DbContext(options)
{
    // Resolved once per DbContext instance (i.e. once per request scope) rather than inside the
    // query filter itself: EF Core query filters must be plain, translatable LINQ expressions —
    // they cannot call back into arbitrary C# methods like IAccessScopeAccessor.CanQuery. These
    // scalar properties are safe to reference directly inside HasQueryFilter (see
    // ScopedResourceQueryFilterExtensions), the same way a `TenantId` column-per-context property
    // is a standard, fully translatable EF Core multi-tenancy pattern.
    private readonly DbContextAccessScope _scopeState =
        accessScopeAccessor?.ResolveDbContextScope() ?? DbContextAccessScope.Unrestricted();

    internal bool ScopeBypassFilters => _scopeState.BypassFilters;
    internal bool ScopeIsAuthenticated => _scopeState.IsAuthenticated;
    internal AccountClass ScopeAccountClass => _scopeState.AccountClass;
    internal string? ScopeTenantDatabaseName => _scopeState.TenantDatabaseName;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<UserSubject> UserSubjects => Set<UserSubject>();
    public DbSet<UserEffectiveMask> UserEffectiveMasks => Set<UserEffectiveMask>();
    public DbSet<UserSubjectExpertiseMask> UserSubjectExpertiseMasks => Set<UserSubjectExpertiseMask>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMentionNotification> ChatMentionNotifications => Set<ChatMentionNotification>();
    public DbSet<CustomChannel> CustomChannels => Set<CustomChannel>();
    public DbSet<CustomChannelAccessRule> CustomChannelAccessRules => Set<CustomChannelAccessRule>();
    public DbSet<InfoEntry> InfoEntries => Set<InfoEntry>();
    public DbSet<TicketPortalConfig> TicketPortalConfigs => Set<TicketPortalConfig>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketUserWatch> TicketUserWatches => Set<TicketUserWatch>();
    public DbSet<TicketMessageScore> TicketMessageScores => Set<TicketMessageScore>();
    public DbSet<TicketModelTrainingExample> TicketModelTrainingExamples => Set<TicketModelTrainingExample>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    public DbSet<ChatMessageAttachment> ChatMessageAttachments => Set<ChatMessageAttachment>();
    public DbSet<ChatMessageVote> ChatMessageVotes => Set<ChatMessageVote>();
    public DbSet<ChatLinkPreview> ChatLinkPreviews => Set<ChatLinkPreview>();
    public DbSet<CandidateApplication> CandidateApplications => Set<CandidateApplication>();
    public DbSet<AssessmentEvent> AssessmentEvents => Set<AssessmentEvent>();
    public DbSet<AssessmentCompetencyEvidence> AssessmentCompetencyEvidence => Set<AssessmentCompetencyEvidence>();
    public DbSet<CandidateCompetencyState> CandidateCompetencyStates => Set<CandidateCompetencyState>();
    public DbSet<CandidateDecision> CandidateDecisions => Set<CandidateDecision>();
    public DbSet<VectorDocument> VectorDocuments => Set<VectorDocument>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Email_Lower", "\"Email\" = lower(\"Email\")"));
            e.HasKey(u => u.UserId);
            e.Property(u => u.UserId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(u => u.Email).HasMaxLength(320).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.CreatedAt).IsRequired();
            e.Property(u => u.UpdatedAt).IsRequired();
        });

        mb.Entity<Role>(e =>
        {
            e.HasKey(r => r.RoleId);
            e.Property(r => r.RoleId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(r => r.Name).IsUnique();
            e.Property(r => r.RoleMask).HasBitColumn("bit(64)", 64);
            e.Property(r => r.PermissionMask).HasBitColumn("bit(256)", 256);
            e.Property(r => r.FeatureMask).HasBitColumn("bit(256)", 256);
            e.Property(r => r.Description);
            e.Property(r => r.IsCustom).HasDefaultValue(false);
            e.Property(r => r.CreatedAtUtc).IsRequired();
            e.Property(r => r.ClaimHostRoomId).HasMaxLength(128);
            e.Property(r => r.ClaimDisplayOrder).HasDefaultValue(0);
            e.Property(r => r.IconName).HasMaxLength(64);
            e.Property(r => r.MessageColor).HasMaxLength(7);
            e.Property(r => r.IsMentionableByUsers).HasDefaultValue(false);
            e.Property(r => r.OwnerAccountClass)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
        });

        mb.Entity<UserRole>(e =>
        {
            e.HasKey(ur => new { ur.UserId, ur.RoleId });
            e.Property(ur => ur.AssignedAt).IsRequired();
            e.HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ur => ur.AssignedByUser)
                .WithMany()
                .HasForeignKey(ur => ur.AssignedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<Permission>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_Permissions_PermissionId_Range", "\"PermissionId\" BETWEEN 0 AND 255"));
            e.HasKey(p => p.PermissionId);
            e.Property(p => p.PermissionId).ValueGeneratedNever();
            e.Property(p => p.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(p => p.IsReserved).HasDefaultValue(false);
            e.Property(p => p.Category).HasMaxLength(64);
        });

        mb.Entity<RolePermission>(e =>
        {
            e.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            e.HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Subject>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Subjects_BitIndex_Range", "\"BitIndex\" BETWEEN 0 AND 127");
                t.HasCheckConstraint("CK_Subjects_SubjectMask_Allowed", SubjectMaskAllowedSql());
            });
            e.HasKey(s => s.SubjectId);
            e.Property(s => s.SubjectId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.SubjectMask).HasMaxLength(64).IsRequired();
            e.Property(s => s.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(s => new { s.SubjectMask, s.BitIndex }).IsUnique();
            e.HasOne(s => s.ParentSubject)
                .WithMany(s => s.ChildSubjects)
                .HasForeignKey(s => s.ParentSubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<UserSubject>(e =>
        {
            e.HasKey(us => new { us.UserId, us.SubjectId });
            e.Property(us => us.AssignedAt).IsRequired();
            e.HasOne(us => us.User)
                .WithMany(u => u.UserSubjects)
                .HasForeignKey(us => us.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(us => us.Subject)
                .WithMany(s => s.UserSubjects)
                .HasForeignKey(us => us.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(us => us.AssignedByUser)
                .WithMany()
                .HasForeignKey(us => us.AssignedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<UserEffectiveMask>(e =>
        {
            e.HasKey(m => m.UserId);
            e.Property(m => m.EffectiveRoleMask).HasBitColumn("bit(64)", 64);
            e.Property(m => m.EffectiveModerationMask).HasBitColumn("bit(256)", 256);
            e.Property(m => m.EffectiveFeatureMask).HasBitColumn("bit(256)", 256);
            e.Property(m => m.GeneralSubjectMask).HasBitColumn("bit(128)", 128);
            e.Property(m => m.StatusMask).HasBitColumn("bit(64)", 64);
            e.Property(m => m.UpdatedAt).IsRequired();
            e.HasOne(m => m.User)
                .WithOne(u => u.EffectiveMask)
                .HasForeignKey<UserEffectiveMask>(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<UserSubjectExpertiseMask>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_UserSubjectExpertiseMasks_Category_Allowed", ExpertiseCategoryAllowedSql()));
            e.HasKey(m => new { m.UserId, m.Category });
            e.Property(m => m.Category).HasMaxLength(64).IsRequired();
            e.Property(m => m.ExpertiseMask).HasBitColumn("bit(128)", 128);
            e.HasOne(m => m.EffectiveMask)
                .WithMany(m => m.SubjectExpertiseMasks)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<RefreshToken>(e =>
        {
            e.HasKey(rt => rt.Id);
            e.Property(rt => rt.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(rt => rt.TokenHash).HasColumnName("TokenHash").IsRequired();
            e.HasIndex(rt => rt.TokenHash).IsUnique();
            e.Property(rt => rt.ExpiresAt).IsRequired();
            e.Property(rt => rt.CreatedAt).IsRequired();
            e.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ChatMessage>(e =>
        {
            e.HasKey(m => m.MessageId);
            e.Property(m => m.MessageId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.RoomId).HasMaxLength(128).IsRequired();
            e.Property(m => m.SenderUsername).HasMaxLength(64).IsRequired();
            e.Property(m => m.SenderMessageColor).HasMaxLength(7);
            e.Property(m => m.RawContent).IsRequired();
            e.Property(m => m.CreatedAtUtc).IsRequired();
            e.Property(m => m.OwnerAccountClass)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(m => m.TenantDatabaseName).HasMaxLength(128);
            e.Property(m => m.ReplyToSenderUsername).HasMaxLength(64);
            e.Property(m => m.ReplyToContentSnippet).HasMaxLength(200);
            e.HasIndex(m => new { m.RoomId, m.CreatedAtUtc });
            e.HasIndex(m => m.SenderId);
            e.HasIndex(m => m.ReplyToMessageId);
        });

        mb.Entity<ChatMentionNotification>(e =>
        {
            e.HasKey(n => n.NotificationId);
            e.Property(n => n.NotificationId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(n => n.SenderUsername).HasMaxLength(64).IsRequired();
            e.Property(n => n.RoomId).HasMaxLength(128).IsRequired();
            e.Property(n => n.RoomDisplayName).HasMaxLength(128).IsRequired();
            e.Property(n => n.CategoryKey).HasMaxLength(64).IsRequired();
            e.Property(n => n.CategoryDisplayName).HasMaxLength(128).IsRequired();
            e.Property(n => n.MessageContent).IsRequired();
            e.Property(n => n.MentionKind).HasMaxLength(32).IsRequired();
            e.Property(n => n.CreatedAtUtc).IsRequired();
            e.Property(n => n.OwnerAccountClass)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(n => n.TenantDatabaseName).HasMaxLength(128);
            e.HasIndex(n => new { n.RecipientUserId, n.ReadAtUtc });
            e.HasIndex(n => new { n.RecipientUserId, n.CreatedAtUtc });
            e.HasIndex(n => new { n.RecipientUserId, n.CategoryKey });
            e.HasIndex(n => n.MessageId);
        });

        mb.Entity<CustomChannel>(e =>
        {
            e.HasKey(c => c.ChannelId);
            e.Property(c => c.ChannelId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.RoomId).HasMaxLength(128).IsRequired();
            e.HasIndex(c => c.RoomId).IsUnique();
            e.Property(c => c.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(c => c.IconName).HasMaxLength(64);
            e.Property(c => c.CategoryKey).HasMaxLength(64).IsRequired();
            e.Property(c => c.CategoryDisplayName).HasMaxLength(128).IsRequired();
            e.Property(c => c.RoomType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(c => c.TieType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(c => c.TieSubjectMask).HasMaxLength(64);
            e.Property(c => c.CreatedAtUtc).IsRequired();
            e.Property(c => c.UpdatedAtUtc).IsRequired();
            e.Property(c => c.OwnerAccountClass)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(c => c.IsArchived).HasDefaultValue(false);
        });

        mb.Entity<CustomChannelAccessRule>(e =>
        {
            e.HasKey(r => r.AccessRuleId);
            e.Property(r => r.AccessRuleId).HasDefaultValueSql("gen_random_uuid()");
            e.HasOne(r => r.Channel)
                .WithMany(c => c.AccessRules)
                .HasForeignKey(r => r.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.CustomRole)
                .WithMany()
                .HasForeignKey(r => r.CustomRoleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.ChannelId);
        });

        mb.Entity<TicketPortalConfig>(e =>
        {
            e.HasKey(p => p.ChannelId);
            e.Property(p => p.CtaLabel).HasMaxLength(128).IsRequired();
            e.Property(p => p.Purpose).HasMaxLength(128).IsRequired();
            e.Property(p => p.FilterName).HasMaxLength(64).IsRequired();
            e.Property(p => p.TrackingMode).HasMaxLength(32).IsRequired();
            e.Property(p => p.DecisionLabelsJson).IsRequired();
            e.Property(p => p.MentionRoleRulesJson).IsRequired();
            e.Property(p => p.StaffAccessRulesJson).IsRequired();
            e.Property(p => p.IntakeSchemaJson).IsRequired();
            e.Property(p => p.UpdatedAtUtc).IsRequired();
            e.HasOne(p => p.Channel)
                .WithOne()
                .HasForeignKey<TicketPortalConfig>(p => p.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Ticket>(e =>
        {
            e.HasKey(t => t.TicketId);
            e.Property(t => t.TicketId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.RoomId).HasMaxLength(128).IsRequired();
            e.Property(t => t.Purpose).HasMaxLength(128).IsRequired();
            e.Property(t => t.FilterName).HasMaxLength(64).IsRequired();
            e.Property(t => t.IntakeAnswersJson).IsRequired();
            e.Property(t => t.ApprovedDecision).HasMaxLength(128);
            e.HasIndex(t => t.RoomId).IsUnique();
            e.HasIndex(t => new { t.PortalChannelId, t.DisplayNumber }).IsUnique();
            e.HasOne(t => t.Portal)
                .WithMany()
                .HasForeignKey(t => t.PortalChannelId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ChatChannel)
                .WithOne()
                .HasForeignKey<Ticket>(t => t.ChatChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<TicketUserWatch>(e =>
        {
            e.HasKey(w => w.WatchId);
            e.Property(w => w.WatchId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(w => w.ContextLabel).HasMaxLength(128).IsRequired();
            e.Property(w => w.Source).HasMaxLength(32).IsRequired();
            e.HasOne(w => w.Ticket)
                .WithMany(t => t.Watches)
                .HasForeignKey(w => w.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => w.TicketId);
            e.HasIndex(w => new { w.TrackedUserId, w.IsActive });
        });

        mb.Entity<TicketMessageScore>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TicketMessageScores_PreviousScore", "\"PreviousScore\" >= 0 AND \"PreviousScore\" <= 1");
                t.HasCheckConstraint("CK_TicketMessageScores_ScoreDelta", "\"ScoreDelta\" >= -1 AND \"ScoreDelta\" <= 1");
                t.HasCheckConstraint("CK_TicketMessageScores_CurrentScore", "\"CurrentScore\" >= 0 AND \"CurrentScore\" <= 1");
                t.HasCheckConstraint("CK_TicketMessageScores_EvidenceConfidence", "\"EvidenceConfidence\" >= 0 AND \"EvidenceConfidence\" <= 1");
                t.HasCheckConstraint("CK_TicketMessageScores_Relevance", "\"Relevance\" >= 0 AND \"Relevance\" <= 1");
                t.HasCheckConstraint("CK_TicketMessageScores_Student", "\"StudentScore\" >= 0 AND \"StudentScore\" <= 1 AND \"StudentConfidence\" >= 0 AND \"StudentConfidence\" <= 1 AND \"StudentRelevance\" >= 0 AND \"StudentRelevance\" <= 1");
            });
            e.HasKey(s => s.ScoreEventId);
            e.Property(s => s.ScoreEventId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.Reason).HasMaxLength(500).IsRequired();
            e.Property(s => s.EvaluatorModelVersion).HasMaxLength(128).IsRequired();
            e.Property(s => s.RawEvaluationJson).IsRequired();
            e.Property(s => s.StudentCategory).HasMaxLength(64).IsRequired();
            e.Property(s => s.StudentReasoning).HasMaxLength(500).IsRequired();
            e.Property(s => s.ReviewerExplanation).HasMaxLength(500);
            e.Property(s => s.ReviewerGuidance).HasMaxLength(500);
            e.HasOne(s => s.Ticket)
                .WithMany(t => t.MessageScores)
                .HasForeignKey(s => s.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.TicketId, s.MessageId }).IsUnique();
            e.HasIndex(s => new { s.TicketId, s.TrackedUserId, s.CreatedAtUtc });
        });

        mb.Entity<TicketModelTrainingExample>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_TicketModelTrainingExamples_Targets", "\"TargetScore\" >= 0 AND \"TargetScore\" <= 1 AND \"TargetRelevance\" >= 0 AND \"TargetRelevance\" <= 1"));
            e.HasKey(x => x.TrainingExampleId);
            e.Property(x => x.TrainingExampleId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Requirement).HasMaxLength(4000).IsRequired();
            e.Property(x => x.BootstrapMessage).HasMaxLength(4000);
            e.Property(x => x.Category).HasMaxLength(64).IsRequired();
            e.Property(x => x.Source).HasMaxLength(32).IsRequired();
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TicketMessageScore>().WithOne().HasForeignKey<TicketModelTrainingExample>(x => x.ScoreEventId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => x.ScoreEventId).IsUnique();
        });

        mb.Entity<ChatAttachment>(e =>
        {
            e.HasKey(a => a.AttachmentId);
            e.Property(a => a.AttachmentId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.OriginalFileName).HasMaxLength(256).IsRequired();
            e.Property(a => a.ContentType).HasMaxLength(128).IsRequired();
            e.Property(a => a.StoragePath).HasMaxLength(512).IsRequired();
            e.Property(a => a.InlinePreviewKind).HasMaxLength(16);
            e.Property(a => a.ScanStatus)
                .HasConversion<string>()
                .HasMaxLength(16)
                .HasDefaultValue(HomeworkCentral.Api.Uploads.MalwareScanResult.Unknown)
                .IsRequired();
        });

        mb.Entity<ChatMessageAttachment>(e =>
        {
            e.HasKey(x => new { x.MessageId, x.AttachmentId });
            e.HasOne(x => x.Message).WithMany(m => m.Attachments).HasForeignKey(x => x.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Attachment).WithMany(a => a.MessageLinks).HasForeignKey(x => x.AttachmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ChatMessageVote>(e =>
        {
            e.HasKey(v => new { v.MessageId, v.UserId });
            e.Property(v => v.Value).IsRequired();
            e.HasOne(v => v.Message).WithMany(m => m.Votes).HasForeignKey(v => v.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => v.MessageId);
        });

        mb.Entity<ChatLinkPreview>(e =>
        {
            e.HasKey(p => p.PreviewId);
            e.Property(p => p.PreviewId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Url).HasMaxLength(2048).IsRequired();
            e.Property(p => p.Title).HasMaxLength(512);
            e.HasOne(p => p.Message).WithMany(m => m.LinkPreviews).HasForeignKey(p => p.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<CandidateApplication>(e =>
        {
            e.HasKey(a => a.CandidateApplicationId);
            e.Property(a => a.CandidateApplicationId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.PositionId).HasMaxLength(64).IsRequired();
            e.Property(a => a.Status).HasMaxLength(64).IsRequired();
            e.HasIndex(a => new { a.UserId, a.PositionId, a.Status });
            e.HasOne(a => a.Ticket).WithMany().HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<AssessmentEvent>(e =>
        {
            e.HasKey(x => x.AssessmentEventId);
            e.Property(x => x.AssessmentEventId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.RubricVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.EvaluatorModelVersion).HasMaxLength(128).IsRequired();
            e.Property(x => x.RawEvaluationJson).IsRequired();
            e.HasOne(x => x.Application).WithMany(a => a.Events).HasForeignKey(x => x.CandidateApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CandidateApplicationId);
        });

        mb.Entity<AssessmentCompetencyEvidence>(e =>
        {
            e.HasKey(x => new { x.AssessmentEventId, x.CompetencyId });
            e.Property(x => x.CompetencyId).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Event).WithMany(ev => ev.CompetencyEvidence).HasForeignKey(x => x.AssessmentEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<CandidateCompetencyState>(e =>
        {
            e.HasKey(x => new { x.CandidateApplicationId, x.CompetencyId });
            e.Property(x => x.CompetencyId).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Application).WithMany(a => a.CompetencyStates)
                .HasForeignKey(x => x.CandidateApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<CandidateDecision>(e =>
        {
            e.HasKey(x => x.CandidateDecisionId);
            e.Property(x => x.CandidateDecisionId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Decision).HasMaxLength(64).IsRequired();
            e.Property(x => x.TriggeredBy).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Application).WithMany(a => a.Decisions)
                .HasForeignKey(x => x.CandidateApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<VectorDocument>(e =>
        {
            e.HasKey(x => x.DocumentId);
            e.Property(x => x.DocumentId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Namespace).HasMaxLength(64).IsRequired();
            e.Property(x => x.PositionId).HasMaxLength(64);
            e.Property(x => x.MetadataJson).IsRequired();
            e.Property(x => x.ContentText).IsRequired();
            e.Property(x => x.EmbeddingJson).IsRequired();
            e.HasIndex(x => x.Namespace);
            e.HasIndex(x => x.CanonicalRecordId);
        });

        mb.Entity<InfoEntry>(e =>
        {
            e.HasKey(i => i.EntryId);
            e.Property(i => i.EntryId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.AuthorUsername).HasMaxLength(64).IsRequired();
            e.Property(i => i.Content).IsRequired();
            e.Property(i => i.CreatedAtUtc).IsRequired();
            e.Property(i => i.UpdatedAtUtc).IsRequired();
            e.HasOne(i => i.Channel)
                .WithMany()
                .HasForeignKey(i => i.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => i.ChannelId);
        });

        mb.ApplyScopedResourceFilters(this);
    }

    private static string SubjectMaskAllowedSql()
    {
        string[] allowed =
        [
            SubjectMaskNames.General,
            .. SubjectExpertiseCatalog.AllExpertiseCategoryNames(),
        ];
        return $"\"SubjectMask\" IN ({string.Join(", ", allowed.Select(v => $"'{v}'"))})";
    }

    private static string ExpertiseCategoryAllowedSql()
    {
        string[] allowed = SubjectExpertiseCatalog.AllExpertiseCategoryNames().ToArray();
        return $"\"Category\" IN ({string.Join(", ", allowed.Select(v => $"'{v}'"))})";
    }
}
