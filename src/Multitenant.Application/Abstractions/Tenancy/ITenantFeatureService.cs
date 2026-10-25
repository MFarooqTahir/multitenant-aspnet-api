using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Application.Abstractions.Tenancy;

/// <summary>
/// Reads and writes the per-tenant feature switches. A feature with no stored row is
/// enabled, so flags act as per-tenant kill switches and never change the behaviour
/// of tenants that have not opted in.
/// </summary>
public interface ITenantFeatureService
{
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantFeatureResponse>> ListAsync(CancellationToken cancellationToken);

    Task<TenantFeatureResponse> SetAsync(string key, bool isEnabled, CancellationToken cancellationToken);
}
