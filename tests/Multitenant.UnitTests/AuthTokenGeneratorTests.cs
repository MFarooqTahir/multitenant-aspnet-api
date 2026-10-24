using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Configuration;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Identity;

namespace Multitenant.UnitTests;

public sealed class JwtTokenGeneratorTests
{
    private static (JwtTokenGenerator Generator, JwtSecurityTokenHandler Handler) Create()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "multitenant-aspnet-api",
            Audience = "multitenant-aspnet-api",
            SigningKey = "unit-test-signing-key-that-is-long-enough-0123456789",
            AccessTokenMinutes = 15,
        });

        var authOptions = Options.Create(new AuthOptions
        {
            PublicBaseUrl = "http://localhost:5270",
            RefreshTokenDays = 30,
            TwoFactorChallengeMinutes = 5,
        });

        var generator = new JwtTokenGenerator(jwtOptions, authOptions, TimeProvider.System);
        return (generator, new JwtSecurityTokenHandler());
    }

    [Fact]
    public void AccessTokenCarriesTenantAndRoleClaims()
    {
        var (generator, _) = Create();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserName = "acme:owner@acme.test",
            Email = "owner@acme.test",
            DisplayName = "Acme Owner",
            SecurityStamp = "stamp-1",
        };

        var accessToken = generator.CreateAccessToken(user, "Owner", "acme");

        Assert.False(string.IsNullOrWhiteSpace(accessToken.Value));
        Assert.True(accessToken.ExpiresAtUtc > TimeProvider.System.GetUtcNow());

        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = tokenHandler.ValidateToken(accessToken.Value, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "multitenant-aspnet-api",
            ValidateAudience = true,
            ValidAudience = "multitenant-aspnet-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("unit-test-signing-key-that-is-long-enough-0123456789")),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role",
        }, out _);

        Assert.Equal(user.Id, Guid.Parse(principal.FindFirst("sub")!.Value));
        Assert.Equal(user.TenantId, Guid.Parse(principal.FindFirst("tenant_id")!.Value));
        Assert.Equal("Owner", principal.FindFirst("role")!.Value);
        Assert.Equal("stamp-1", principal.FindFirst("sstamp")!.Value);
    }

    [Fact]
    public void TwoFactorChallengeCarriesTenantAndPurpose()
    {
        var (generator, _) = Create();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserName = "acme:owner@acme.test",
            Email = "owner@acme.test",
            SecurityStamp = "stamp-1",
        };

        var challenge = generator.CreateTwoFactorChallenge(user);
        Assert.True(challenge.ExpiresAtUtc > TimeProvider.System.GetUtcNow());

        Assert.True(generator.TryValidateTwoFactorChallenge(challenge.Value, out var userId, out var tenantId));
        Assert.Equal(user.Id, userId);
        Assert.Equal(user.TenantId, tenantId);
    }

    [Fact]
    public void TamperedTwoFactorTokenIsRejected()
    {
        var (generator, _) = Create();
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = "s" };
        var challenge = generator.CreateTwoFactorChallenge(user);

        var tampered = challenge.Value + "x";
        Assert.False(generator.TryValidateTwoFactorChallenge(tampered, out _, out _));
    }
}
