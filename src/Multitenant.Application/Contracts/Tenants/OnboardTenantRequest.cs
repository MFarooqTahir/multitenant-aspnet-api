namespace Multitenant.Application.Contracts.Tenants;

/// <summary>Self-service tenant provisioning. Creates the first Owner and starter data.</summary>
public sealed record OnboardTenantRequest(
    string Slug,
    string Name,
    string OwnerEmail,
    string OwnerDisplayName,
    string OwnerPassword);
