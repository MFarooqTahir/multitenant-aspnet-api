namespace Multitenant.Application.Contracts.Results;

/// <summary>
/// Outcome of a login attempt: either a token pair or a two-factor challenge.
/// </summary>
public abstract record LoginResult;
