namespace Multitenant.Application.Contracts;

public sealed record LoginRequest(string TenantSlug, string Email, string Password);
