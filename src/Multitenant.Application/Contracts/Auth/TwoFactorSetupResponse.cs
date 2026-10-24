namespace Multitenant.Application.Contracts.Auth;

/// <summary>Authenticator key and <c>otpauth://</c> URI for the user to scan.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);
