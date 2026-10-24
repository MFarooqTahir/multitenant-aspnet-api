using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Tenancy;

public sealed class TenantStore(AppDbContext dbContext) : ITenantStore
{
    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = TenantSlug.Normalize(slug);
        return dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == normalized, cancellationToken);
    }
}
