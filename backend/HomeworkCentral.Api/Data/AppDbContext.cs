using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public partial class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IAccessScopeAccessor? accessScopeAccessor = null) : DbContext(options)
{
    internal IAccessScopeAccessor? AccessScopeAccessor => accessScopeAccessor;

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
            e.Property(m => m.RawContent).IsRequired();
            e.Property(m => m.CreatedAtUtc).IsRequired();
            e.Property(m => m.OwnerAccountClass)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            e.Property(m => m.TenantDatabaseName).HasMaxLength(128);
            e.HasIndex(m => new { m.RoomId, m.CreatedAtUtc });
            e.HasIndex(m => m.SenderId);
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
