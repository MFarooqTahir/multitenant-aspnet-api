namespace Multitenant.Application.Contracts.Auth;

/// <summary>Recovery codes returned once, immediately after enabling two-factor.</summary>
public sealed record TwoFactorEnabledResponse(IReadOnlyList<string> RecoveryCodes);
