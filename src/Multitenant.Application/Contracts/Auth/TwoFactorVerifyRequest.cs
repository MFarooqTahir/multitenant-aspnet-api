namespace Multitenant.Application.Contracts.Auth;

public sealed record TwoFactorVerifyRequest(string TwoFactorToken, string? Code, string? RecoveryCode);
