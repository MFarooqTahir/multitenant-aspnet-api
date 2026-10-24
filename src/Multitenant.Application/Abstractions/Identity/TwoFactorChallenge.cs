namespace Multitenant.Application.Abstractions;

/// <summary>
/// Short-lived token proving that a password was verified but the second factor
/// has not been supplied yet.
/// </summary>
public sealed record TwoFactorChallenge(string Value, DateTimeOffset ExpiresAtUtc);
