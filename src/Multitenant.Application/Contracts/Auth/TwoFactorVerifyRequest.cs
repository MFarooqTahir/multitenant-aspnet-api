namespace Multitenant.Application.Contracts;

public sealed record TwoFactorVerifyRequest(string TwoFactorToken, string? Code, string? RecoveryCode);
