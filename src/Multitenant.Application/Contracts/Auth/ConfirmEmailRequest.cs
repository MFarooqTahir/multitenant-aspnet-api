namespace Multitenant.Application.Contracts.Auth;

public sealed record ConfirmEmailRequest(Guid UserId, string Token);
