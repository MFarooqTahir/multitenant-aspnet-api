namespace Multitenant.Application.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }

    string? Slug { get; }

    bool IsResolved { get; }

    void SetTenant(Guid tenantId, string slug);
}
