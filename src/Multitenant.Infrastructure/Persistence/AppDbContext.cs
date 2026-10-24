using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Global query filters must reference a DbContext member (not the injected
    /// <see cref="ITenantContext"/> closure) because EF Core caches the model per
    /// context type. A captured closure would freeze the tenant of the first
    /// DbContext instance that built the model for the process lifetime.
    /// </summary>
    public Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
        });

        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AspNetUsers");
            entity.Property(x => x.UserName).HasMaxLength(320);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(320);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(256);
            entity.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.TenantId).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();

            // Composite key target for tenant-safe foreign keys (task assignee).
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });

            entity.HasOne(x => x.Tenant)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        builder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.Name });
            entity.HasAlternateKey(x => new { x.TenantId, x.Id });

            entity.HasOne<Tenant>()
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        builder.Entity<WorkItem>(entity =>
        {
            entity.ToTable("WorkItems", table => table.HasCheckConstraint(
                "CK_WorkItems_AssigneeTenant",
                "\"AssigneeTenantId\" IS NULL OR \"AssigneeTenantId\" = \"TenantId\""));

            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.DueDate);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.ProjectId });
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.Priority });
            entity.HasIndex(x => new { x.TenantId, x.AssigneeUserId });

            entity.HasOne(x => x.Project)
                .WithMany(x => x.Tasks)
                .HasForeignKey(x => new { x.TenantId, x.ProjectId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Assignee)
                .WithMany()
                .HasForeignKey(x => new { x.AssigneeTenantId, x.AssigneeUserId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedByIp).HasMaxLength(64);
            entity.Property(x => x.RevokedReason).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.ExpiresAtUtc).IsRequired();

            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.FamilyId);
            entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
