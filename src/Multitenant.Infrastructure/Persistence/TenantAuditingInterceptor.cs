using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Persistence;

public sealed class TenantAuditingInterceptor(ITenantContext tenantContext, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned tenantOwned)
            {
                GuardTenant(entry, tenantOwned);
            }

            if (entry.Entity is IHasTimestamps timestamps)
            {
                if (entry.State == EntityState.Added)
                {
                    timestamps.CreatedAtUtc = now;
                    timestamps.UpdatedAtUtc = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    timestamps.UpdatedAtUtc = now;
                }
            }
        }
    }

    private void GuardTenant(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ITenantOwned entity)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            return;
        }

        if (!tenantContext.IsResolved || tenantContext.TenantId is null)
        {
            throw new InvalidOperationException(
                "A tenant-owned entity was written without a resolved tenant context.");
        }

        if (entity.TenantId == Guid.Empty)
        {
            if (entry.State != EntityState.Added)
            {
                throw new ForbiddenException("Tenant-owned data cannot be modified across tenants.", "tenant_mismatch");
            }

            entity.TenantId = tenantContext.TenantId.Value;
            return;
        }

        if (entity.TenantId != tenantContext.TenantId.Value)
        {
            throw new ForbiddenException("Tenant-owned data cannot be modified across tenants.", "tenant_mismatch");
        }
    }
}
