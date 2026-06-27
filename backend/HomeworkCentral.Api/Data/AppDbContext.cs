using HomeworkCentral.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeworkCentral.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
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
            e.Property(r => r.PermissionMask)
                .HasColumnType("bit(256)")
                .IsRequired()
                .HasConversion(
                    v => v,
                    v => v ?? new System.Collections.BitArray(256));
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
            e.HasKey(p => p.PermissionId);
            e.Property(p => p.Name).HasMaxLength(64).IsRequired();
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(p => p.IsReserved).HasDefaultValue(false);
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

        mb.Entity<RefreshToken>(e =>
        {
            e.HasKey(rt => rt.Id);
            e.Property(rt => rt.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(rt => rt.Token).IsRequired();
            e.HasIndex(rt => rt.Token).IsUnique();
            e.Property(rt => rt.ExpiresAt).IsRequired();
            e.Property(rt => rt.CreatedAt).IsRequired();
            e.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
