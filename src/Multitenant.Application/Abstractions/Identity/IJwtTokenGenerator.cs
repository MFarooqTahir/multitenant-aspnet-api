using Multitenant.Domain.Entities;

namespace Multitenant.Application.Abstractions;

/// <summary>Creates and validates the JWTs the API issues.</summary>
public interface IJwtTokenGenerator
{
    AccessToken CreateAccessToken(AppUser user, string role, string tenantSlug);

    TwoFactorChallenge CreateTwoFactorChallenge(AppUser user);

    bool TryValidateTwoFactorChallenge(string token, out Guid userId, out Guid tenantId);
}
