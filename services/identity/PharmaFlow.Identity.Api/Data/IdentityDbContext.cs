using Microsoft.EntityFrameworkCore;
using PharmaFlow.Identity.Api.Domain;

namespace PharmaFlow.Identity.Api.Data;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserBranchAssignment> UserBranchAssignments => Set<UserBranchAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<SystemBootstrapState> SystemBootstrapStates => Set<SystemBootstrapState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.FullName).HasMaxLength(256).IsRequired();
            b.Property(u => u.ActorType).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(r => r.Name).IsUnique();
            b.Property(r => r.Description).HasMaxLength(256);
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(ur => new { ur.UserId, ur.RoleId });
            b.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
            b.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions");
            b.HasKey(rp => rp.Id);
            b.Property(rp => rp.Permission).HasMaxLength(128).IsRequired();
            b.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId);
            b.HasIndex(rp => new { rp.RoleId, rp.Permission }).IsUnique();
        });

        modelBuilder.Entity<UserBranchAssignment>(b =>
        {
            b.ToTable("user_branch_assignments");
            b.HasKey(uba => uba.Id);
            b.HasOne(uba => uba.User).WithMany(u => u.BranchAssignments).HasForeignKey(uba => uba.UserId);
            b.HasIndex(uba => new { uba.UserId, uba.BranchId }).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(rt => rt.Id);
            b.Property(rt => rt.Token).HasMaxLength(512).IsRequired();
            b.HasIndex(rt => rt.Token);
            b.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens).HasForeignKey(rt => rt.UserId);
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.ToTable("password_reset_tokens");
            b.HasKey(prt => prt.Id);
            b.Property(prt => prt.TokenHash).HasMaxLength(256).IsRequired();
            b.HasOne(prt => prt.User).WithMany().HasForeignKey(prt => prt.UserId);
        });

        modelBuilder.Entity<SystemBootstrapState>(b =>
        {
            b.ToTable("system_bootstrap_states");
            b.HasKey(s => s.Id);
        });
    }
}
