using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed record TwoFactorChallenge(string Value, DateTimeOffset ExpiresAtUtc);

public interface IJwtTokenGenerator
{
    AccessToken CreateAccessToken(AppUser user, string role, string tenantSlug);

    TwoFactorChallenge CreateTwoFactorChallenge(AppUser user);

    bool TryValidateTwoFactorChallenge(string token, out Guid userId, out Guid tenantId);
}
