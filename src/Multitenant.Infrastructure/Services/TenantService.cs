using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Errors;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

/// <summary>
/// Creates tenants. Everything a new tenant needs — the tenant row, its first
/// Owner, the starter project and starter tasks — is written in one transaction,
/// so a failure never leaves a half-provisioned tenant behind.
/// </summary>
public sealed partial class TenantService(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    IUserProvisioningService userProvisioning,
    IAccountEmailService accountEmailService,
    TimeProvider timeProvider,
    ILogger<TenantService> logger) : ITenantService
{
    public async Task<TenantResponse> OnboardAsync(
        OnboardTenantRequest request,
        CancellationToken cancellationToken)
    {
        var slug = TenantSlug.Normalize(request.Slug);
        TenantSlug.EnsureValid(slug);

        if (await dbContext.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            throw new ConflictException("That tenant slug is already taken.", "slug_taken");
        }

        var tenant = Tenant.Create(slug, request.Name, timeProvider.GetUtcNow());

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        AppUser owner;
        try
        {
            dbContext.Tenants.Add(tenant);
            tenantContext.SetTenant(tenant.Id, tenant.Slug);

            owner = await userProvisioning.CreateAsync(
                tenant.Id,
                tenant.Slug,
                request.OwnerEmail,
                request.OwnerDisplayName,
                request.OwnerPassword,
                TenantRoles.Owner,
                cancellationToken);

            AddStarterData(tenant);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogOnboardingFailed(logger, ex, slug);

            throw new ConflictException(
                "The tenant could not be created. The slug or email may already be in use.",
                "tenant_conflict");
        }

        // Email is a side effect and must not be part of the transaction.
        await accountEmailService.SendConfirmationEmailAsync(owner, cancellationToken);

        return ToResponse(tenant);
    }

    public async Task<TenantResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant not found.");

        return ToResponse(tenant);
    }

    private void AddStarterData(Tenant tenant)
    {
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
    }

    private static TenantResponse ToResponse(Tenant tenant) =>
        new(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAtUtc);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Tenant onboarding failed for slug {Slug}.")]
    private static partial void LogOnboardingFailed(ILogger logger, Exception exception, string slug);
}
