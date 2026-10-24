namespace Multitenant.Application.Abstractions;

/// <summary>
/// Request-scoped ambient state: which tenant the current request belongs to.
/// </summary>
/// <remarks>
/// This is not a data-access abstraction — services already use <c>AppDbContext</c>
/// for queries. It exists because two cross-cutting components need to know the
/// tenant without being handed it by every caller:
/// <list type="bullet">
/// <item>EF Core global query filters, which add <c>TenantId = ...</c> to tenant-owned queries.</item>
/// <item>The <c>SaveChanges</c> interceptor, which stamps and guards writes.</item>
/// </list>
/// The value is set once per request by JWT validation (authenticated calls) or by
/// the anonymous flows that resolve a tenant before touching tenant-owned data.
/// </remarks>
public interface ITenantContext
{
    /// <summary>The resolved tenant, or <c>null</c> before resolution.</summary>
    Guid? TenantId { get; }

    /// <summary>The tenant slug when known, used for user-name formatting and diagnostics.</summary>
    string? Slug { get; }

    /// <summary>True once a tenant has been resolved for this request.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Binds the request to a tenant. Called exactly once per request, before any
    /// tenant-owned read or write.
    /// </summary>
    void SetTenant(Guid tenantId, string slug);
}
