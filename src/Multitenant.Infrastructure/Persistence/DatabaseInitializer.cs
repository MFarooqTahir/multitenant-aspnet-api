using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Configuration;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Persistence;

/// <summary>
/// Applies migrations (when enabled) and seeds the three tenant roles plus two
/// demo tenants. Seeds are idempotent: an existing slug is left untouched.
/// </summary>
public sealed partial class DatabaseInitializer(
    AppDbContext dbContext,
    RoleManager<AppRole> roleManager,
    UserManager<AppUser> userManager,
    ITenantContext tenantContext,
    ITenantStore tenantStore,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<DemoOptions> demoOptions,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger)
{
    private readonly DatabaseOptions _database = databaseOptions.Value;
    private readonly DemoOptions _demo = demoOptions.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_database.AutoMigrate)
        {
            LogApplyingMigrations(logger);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        await EnsureRolesAsync();

        if (!_database.SeedDemoData)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_demo.Password))
        {
            LogMissingDemoPassword(logger);
            return;
        }

        await SeedDemoTenantAsync(
            slug: "acme",
            name: "Acme Corporation",
            ownerEmail: "owner@acme.test",
            ownerDisplayName: "Acme Owner",
            memberEmail: "member@acme.test",
            memberDisplayName: "Acme Member",
            projectName: "Apollo",
            cancellationToken);

        await SeedDemoTenantAsync(
            slug: "globex",
            name: "Globex Industries",
            ownerEmail: "owner@globex.test",
            ownerDisplayName: "Globex Owner",
            memberEmail: "member@globex.test",
            memberDisplayName: "Globex Member",
            projectName: "Atlas",
            cancellationToken);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in TenantRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new AppRole(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed role '{role}': {string.Join(" ", result.Errors.Select(x => x.Description))}");
            }
        }
    }

    private async Task SeedDemoTenantAsync(
        string slug,
        string name,
        string ownerEmail,
        string ownerDisplayName,
        string memberEmail,
        string memberDisplayName,
        string projectName,
        CancellationToken cancellationToken)
    {
        var existing = await tenantStore.FindBySlugAsync(slug, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        LogSeedingTenant(logger, slug);

        var tenant = Tenant.Create(slug, name, timeProvider.GetUtcNow());
        dbContext.Tenants.Add(tenant);
        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var owner = await CreateUserAsync(tenant, ownerEmail, ownerDisplayName, TenantRoles.Owner, cancellationToken);
        await CreateUserAsync(tenant, memberEmail, memberDisplayName, TenantRoles.Member, cancellationToken);

        var project = new Project
        {
            TenantId = tenant.Id,
            Name = projectName,
            Description = $"The {projectName} project seeded for the {name} demo tenant.",
            Status = ProjectStatus.Active,
        };

        dbContext.Projects.Add(project);
        dbContext.WorkItems.AddRange(
            new WorkItem
            {
                TenantId = tenant.Id,
                ProjectId = project.Id,
                Title = "Review the onboarding flow",
                Description = "Walk through onboarding as an Owner and confirm the starter data.",
                Status = WorkItemStatus.Done,
                Priority = WorkItemPriority.Medium,
                AssigneeUserId = owner.Id,
            },
            new WorkItem
            {
                TenantId = tenant.Id,
                ProjectId = project.Id,
                Title = "Invite the team",
                Description = "Create Member and Admin users to explore the role policies.",
                Status = WorkItemStatus.InProgress,
                Priority = WorkItemPriority.High,
                AssigneeUserId = owner.Id,
            },
            new WorkItem
            {
                TenantId = tenant.Id,
                ProjectId = project.Id,
                Title = "Ship the first release",
                Description = "Tag v1.0.0 once the API is deployed.",
                Status = WorkItemStatus.Todo,
                Priority = WorkItemPriority.Low,
                DueDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(14),
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AppUser> CreateUserAsync(
        Tenant tenant,
        string email,
        string displayName,
        string role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var existing = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserName = $"{tenant.Slug}:{email.Trim().ToLowerInvariant()}",
            Email = email.Trim(),
            DisplayName = displayName,
            // Demo accounts are pre-confirmed so they work regardless of
            // Identity:RequireConfirmedEmail.
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow(),
        };

        var created = await userManager.CreateAsync(user, _demo.Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed demo user '{email}': {string.Join(" ", created.Errors.Select(x => x.Description))}");
        }

        var added = await userManager.AddToRoleAsync(user, role);
        if (!added.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed role '{role}' for '{email}': {string.Join(" ", added.Errors.Select(x => x.Description))}");
        }

        return user;
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Applying database migrations.")]
    private static partial void LogApplyingMigrations(ILogger logger);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Database:SeedDemoData is enabled but Demo:Password is empty; skipping demo tenants.")]
    private static partial void LogMissingDemoPassword(ILogger logger);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Seeding demo tenant {Slug}.")]
    private static partial void LogSeedingTenant(ILogger logger, string slug);
}
