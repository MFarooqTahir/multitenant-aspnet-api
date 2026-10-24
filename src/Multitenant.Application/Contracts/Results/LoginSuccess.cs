namespace Multitenant.Application.Contracts;

/// <summary>Login completed and tokens were issued.</summary>
public sealed record LoginSuccess(AuthTokens Tokens) : LoginResult;
