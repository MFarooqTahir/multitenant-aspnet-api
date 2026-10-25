namespace Multitenant.Application.Contracts.Tenants;

/// <summary>Turns one whitelisted feature on or off for the current tenant.</summary>
public sealed record UpdateTenantFeatureRequest(bool IsEnabled);
