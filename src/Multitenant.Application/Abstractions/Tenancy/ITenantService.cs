using Multitenant.Application.Contracts;

namespace Multitenant.Application.Abstractions;

/// <summary>
/// Tenant onboarding and current-tenant reads.
/// </summary>
public interface ITenantService
{
    Task<TenantResponse> OnboardAsync(OnboardTenantRequest request, CancellationToken cancellationToken);

    Task<TenantResponse> GetCurrentAsync(CancellationToken cancellationToken);
}
