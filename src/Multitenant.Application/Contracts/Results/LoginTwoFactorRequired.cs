namespace Multitenant.Application.Contracts;

/// <summary>Password was correct, but the client must complete a second factor.</summary>
public sealed record LoginTwoFactorRequired(string TwoFactorToken, int ExpiresIn) : LoginResult;
