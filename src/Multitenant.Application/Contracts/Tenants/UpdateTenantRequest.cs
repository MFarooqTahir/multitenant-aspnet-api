namespace Multitenant.Application.Contracts.Tenants;

/// <summary>Renames the current tenant. The slug is immutable: it is the login key.</summary>
public sealed record UpdateTenantRequest(string Name);
