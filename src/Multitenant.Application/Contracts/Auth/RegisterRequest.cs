namespace Multitenant.Application.Contracts;

public sealed record RegisterRequest(string TenantSlug, string Email, string DisplayName, string Password);
