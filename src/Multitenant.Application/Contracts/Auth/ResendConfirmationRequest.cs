namespace Multitenant.Application.Contracts;

public sealed record ResendConfirmationRequest(string TenantSlug, string Email);
