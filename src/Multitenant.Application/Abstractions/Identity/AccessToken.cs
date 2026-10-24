namespace Multitenant.Application.Abstractions;

/// <summary>An issued access token and the moment it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
