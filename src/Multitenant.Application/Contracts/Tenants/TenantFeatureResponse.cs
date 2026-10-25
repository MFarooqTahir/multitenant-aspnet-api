namespace Multitenant.Application.Contracts.Tenants;

/// <summary>The effective value of one tenant feature flag.</summary>
public sealed record TenantFeatureResponse(string Key, bool IsEnabled);
