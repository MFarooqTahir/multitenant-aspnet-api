namespace Multitenant.Application.Contracts;

/// <summary>An access token plus the refresh token that can replace it.</summary>
public sealed record AuthTokens(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType = "Bearer");
