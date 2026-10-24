namespace Multitenant.Application.Contracts.Auth;

public sealed record LoginRequest(string TenantSlug, string Email, string Password);
