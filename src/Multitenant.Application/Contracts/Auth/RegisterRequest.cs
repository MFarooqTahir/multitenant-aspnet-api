namespace Multitenant.Application.Contracts.Auth;

public sealed record RegisterRequest(string TenantSlug, string Email, string DisplayName, string Password);
