namespace Multitenant.Application.Contracts.Auth;

public sealed record ResendConfirmationRequest(string TenantSlug, string Email);
