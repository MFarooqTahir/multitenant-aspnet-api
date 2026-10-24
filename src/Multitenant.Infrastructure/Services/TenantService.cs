using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Common;
using Multitenant.Application.Contracts;
using Multitenant.Application.Errors;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

public sealed partial class TenantService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    ITenantContext tenantContext,
    IAccountEmailService accountEmailService,

    TimeProvider timeProvider,
    ILogger<TenantService> logger) : ITenantService
{
    public async Task<TenantResponse> OnboardAsync(OnboardTenantRequest request, CancellationToken cancellationToken)
    {
        var slug = TenantSlug.Normalize(request.Slug);
        TenantSlug.EnsureValid(slug);

        if (await dbContext.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            throw new ConflictException("That tenant slug is already taken.", "slug_taken");
        }

        var tenant = Tenant.Create(slug, request.Name, timeProvider.GetUtcNow());
        var email = request.OwnerEmail.Trim();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.Tenants.Add(tenant);
            tenantContext.SetTenant(tenant.Id, tenant.Slug);

            var owner = new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserName = UserNames.For(tenant.Slug, email),
                Email = email,
                DisplayName = request.OwnerDisplayName.Trim(),
                EmailConfirmed = false,
                IsActive = true,
                CreatedAtUtc = timeProvider.GetUtcNow(),
            };

            var created = await userManager.CreateAsync(owner, request.OwnerPassword);
            if (!created.Succeeded)
            {
                throw new ConflictException(
                    string.Join(" ", created.Errors.Select(x => x.Description)),
                    "owner_creation_failed");
            }

            var role = await userManager.AddToRoleAsync(owner, TenantRoles.Owner);
            if (!role.Succeeded)
            {
                throw new ConflictException(
                    string.Join(" ", role.Errors.Select(x => x.Description)),
                    "owner_role_failed");
            }

            var project = new Project
            {
                TenantId = tenant.Id,
                Name = "Getting started",
                Description = "A starter project created during tenant onboarding.",
                Status = ProjectStatus.Active,
            };

            dbContext.Projects.Add(project);
            dbContext.WorkItems.AddRange(
                new WorkItem
                {
                    TenantId = tenant.Id,
                    ProjectId = project.Id,
                    Title = "Invite a teammate",
                    Description = "Add another member to this tenant and assign them a task.",
                    Status = WorkItemStatus.Todo,
                    Priority = WorkItemPriority.Medium,
                },
                new WorkItem
                {
                    TenantId = tenant.Id,
                    ProjectId = project.Id,
                    Title = "Create your first project",
                    Description = "Replace the starter project with something real.",
                    Status = WorkItemStatus.Todo,
                    Priority = WorkItemPriority.Medium,
                },
                new WorkItem
                {
                    TenantId = tenant.Id,
                    ProjectId = project.Id,
                    Title = "Complete onboarding",
                    Description = "Confirm your account details and try the API.",
                    Status = WorkItemStatus.Done,
                    Priority = WorkItemPriority.Low,
                });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await accountEmailService.SendConfirmationEmailAsync(owner, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogOnboardingFailed(logger, ex, slug);
            throw new ConflictException("The tenant could not be created. The slug or email may already be in use.", "tenant_conflict");
        }

        return ToResponse(tenant);
    }

    public async Task<TenantResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new UnauthorizedException();

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant not found.");

        return ToResponse(tenant);
    }

    private static TenantResponse ToResponse(Tenant tenant) =>
        new(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAtUtc);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Tenant onboarding failed for slug {Slug}.")]
    private static partial void LogOnboardingFailed(ILogger logger, Exception exception, string slug);
}
