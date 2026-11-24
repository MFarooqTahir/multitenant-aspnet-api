using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Application.Configuration.Jwt;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Identity;
using Multitenant.UnitTests.Infrastructure;

namespace Multitenant.UnitTests;

public sealed class JwtTokenGeneratorTests
{
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

        Assert.True(generator.TryValidateTwoFactorChallenge(
            challenge.Value,
            out var userId,
            out var tenantId,
            out var securityStamp));
        Assert.Equal(user.Id, userId);
        Assert.Equal(user.TenantId, tenantId);
        Assert.Equal(user.SecurityStamp, securityStamp);
    }

    [Fact]
    public void TamperedTwoFactorTokenIsRejected()
    {
        var (generator, _) = Create();
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = "s" };
        var challenge = generator.CreateTwoFactorChallenge(user);

        var tampered = challenge.Value + "x";
        Assert.False(generator.TryValidateTwoFactorChallenge(tampered, out _, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateAccessToken_NullOrEmptySecurityStamp_Throws(string? securityStamp)
    {
        var (generator, _) = Create();
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = securityStamp! };

        var exception = Assert.Throws<InvalidOperationException>(() => generator.CreateAccessToken(user, "Member", "acme"));

        Assert.Equal("Cannot issue an access token without a security stamp.", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateTwoFactorChallenge_NullOrEmptySecurityStamp_Throws(string? securityStamp)
    {
        var (generator, _) = Create();
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = securityStamp! };

        var exception = Assert.Throws<InvalidOperationException>(() => generator.CreateTwoFactorChallenge(user));

        Assert.Equal("Cannot issue a two-factor challenge without a security stamp.", exception.Message);
    }

    [Fact]
    public void AccessToken_UsesExactLifetimeAndClaimsWithFakeTime()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var (generator, handler) = Create(new FakeTimeProvider(now));
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "owner@acme.test",
            DisplayName = "Owner",
            SecurityStamp = "stamp-1",
        };

        var token = generator.CreateAccessToken(user, TenantRoles.Owner, "acme");
        var jwt = handler.ReadJwtToken(token.Value);

        Assert.Equal(now, token.ExpiresAtUtc.AddMinutes(-15));
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(now.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(user.TenantId.ToString(), jwt.Claims.Single(claim => claim.Type == AuthClaims.TenantId).Value);
        Assert.Equal("acme", jwt.Claims.Single(claim => claim.Type == AuthClaims.TenantSlug).Value);
        Assert.Equal(TenantRoles.Owner, jwt.Claims.Single(claim => claim.Type == AuthClaims.Role).Value);
        Assert.Equal("stamp-1", jwt.Claims.Single(claim => claim.Type == AuthClaims.SecurityStamp).Value);
    }

    [Fact]
    public void TwoFactorChallenge_UsesExactLifetimeAndPurposeWithFakeTime()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var (generator, handler) = Create(timeProvider);
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = "stamp-1" };

        var challenge = generator.CreateTwoFactorChallenge(user);
        var jwt = handler.ReadJwtToken(challenge.Value);

        Assert.Equal(now.AddMinutes(5), challenge.ExpiresAtUtc);
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(now.AddMinutes(5).UtcDateTime, jwt.ValidTo);
        Assert.Equal(AuthClaims.TwoFactorPurpose, jwt.Claims.Single(claim => claim.Type == AuthClaims.Purpose).Value);
        Assert.Equal("stamp-1", jwt.Claims.Single(claim => claim.Type == AuthClaims.SecurityStamp).Value);

        timeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(31)));
        Assert.False(generator.TryValidateTwoFactorChallenge(challenge.Value, out _, out _, out _));
    }

    [Fact]
    public void TryValidateTwoFactorChallenge_WrongPurpose_ReturnsFalse()
    {
        var (generator, _) = Create();
        var user = new AppUser { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), SecurityStamp = "stamp-1" };
        var accessToken = generator.CreateAccessToken(user, TenantRoles.Member, "acme");

        Assert.False(generator.TryValidateTwoFactorChallenge(accessToken.Value, out _, out _, out _));
    }

    [Fact]
    public void TryValidateTwoFactorChallenge_MissingExpiration_ReturnsFalse()
    {
        var (generator, handler) = Create();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(AuthClaims.TenantId, tenantId.ToString()),
            new(AuthClaims.SecurityStamp, "stamp-1"),
            new(AuthClaims.Purpose, AuthClaims.TwoFactorPurpose),
        ];
        var token = new JwtSecurityToken(
            "multitenant-aspnet-api",
            "multitenant-aspnet-api",
            claims,
            notBefore: null,
            expires: null,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("unit-test-signing-key-that-is-long-enough-0123456789")),
                SecurityAlgorithms.HmacSha256));

        Assert.False(generator.TryValidateTwoFactorChallenge(handler.WriteToken(token), out _, out _, out _));
    }

    private static (JwtTokenGenerator Generator, JwtSecurityTokenHandler Handler) Create(TimeProvider? timeProvider = null)
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

        var generator = new JwtTokenGenerator(jwtOptions, authOptions, timeProvider ?? TimeProvider.System);
        return (generator, new JwtSecurityTokenHandler());
    }
}
