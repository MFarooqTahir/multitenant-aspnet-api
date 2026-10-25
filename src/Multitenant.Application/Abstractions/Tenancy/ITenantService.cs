using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Abstractions.Tenancy;

/// <summary>
/// Tenant onboarding and current-tenant reads and updates.
/// </summary>
public interface ITenantService
{
    Task<TenantResponse> OnboardAsync(OnboardTenantRequest request, CancellationToken cancellationToken);

    Task<TenantResponse> GetCurrentAsync(CancellationToken cancellationToken);

    Task<TenantResponse> UpdateCurrentAsync(
        UpdateTenantRequest request,
        CancellationToken cancellationToken);
}
