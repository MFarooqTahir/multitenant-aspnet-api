using Multitenant.Application.Abstractions.Tenancy;

namespace Multitenant.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public string? Slug { get; private set; }

    public bool IsResolved => TenantId.HasValue && TenantId.Value != Guid.Empty;

    public void SetTenant(Guid tenantId, string slug)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        TenantId = tenantId;
        Slug = slug;
    }
}
