using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

/// <summary>Master database context — tenant registry and global metadata only.</summary>
public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.TenantId);
            e.Property(t => t.TenantId).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.DatabaseName).HasMaxLength(64).IsRequired();
            e.HasIndex(t => t.DatabaseName).IsUnique();
            e.Property(t => t.ClusterSlug).HasMaxLength(32).IsRequired();
            e.HasIndex(t => t.ClusterSlug);
            e.Property(t => t.Slug).HasMaxLength(64).IsRequired();
            e.HasIndex(t => new { t.ClusterSlug, t.Slug }).IsUnique();
            e.Property(t => t.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(t => t.DeveloperEmail).HasMaxLength(320).IsRequired();
            e.HasIndex(t => t.DeveloperEmail);
            e.Property(t => t.PersonaEmail).HasMaxLength(320).IsRequired();
            e.HasIndex(t => t.PersonaEmail).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Tenants_DeveloperEmail_Lower", "\"DeveloperEmail\" = lower(\"DeveloperEmail\")");
                t.HasCheckConstraint("CK_Tenants_PersonaEmail_Lower", "\"PersonaEmail\" = lower(\"PersonaEmail\")");
            });
            e.Property(t => t.ClusterEnvironment).HasMaxLength(16).IsRequired();
            e.Property(t => t.CreatedAt).IsRequired();
        });
    }
}
