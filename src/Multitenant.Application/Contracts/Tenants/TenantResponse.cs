namespace Multitenant.Application.Contracts.Tenants;

public sealed record TenantResponse(
    Guid Id,
    string Slug,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);
