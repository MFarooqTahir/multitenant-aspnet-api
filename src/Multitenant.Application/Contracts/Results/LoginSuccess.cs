using Multitenant.Application.Contracts.Auth;

namespace Multitenant.Application.Contracts.Results;

/// <summary>Login completed and tokens were issued.</summary>
public sealed record LoginSuccess(AuthTokens Tokens) : LoginResult;
