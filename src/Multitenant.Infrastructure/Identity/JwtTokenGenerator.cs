using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Identity;

public sealed class JwtTokenGenerator(
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider) : IJwtTokenGenerator
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly AuthOptions _auth = authOptions.Value;

    public AccessToken CreateAccessToken(AppUser user, string role, string tenantSlug)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(AuthClaims.TenantId, user.TenantId.ToString()),
            new(AuthClaims.TenantSlug, tenantSlug),
            new(AuthClaims.Email, user.Email ?? string.Empty),
            new(AuthClaims.Name, user.DisplayName),
            new(AuthClaims.Role, role),
            new(AuthClaims.SecurityStamp, user.SecurityStamp ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = CreateToken(claims, now.UtcDateTime, expires.UtcDateTime);
        return new AccessToken(token, expires);
    }

    public TwoFactorChallenge CreateTwoFactorChallenge(AppUser user)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_auth.TwoFactorChallengeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(AuthClaims.TenantId, user.TenantId.ToString()),
            new(AuthClaims.Purpose, AuthClaims.TwoFactorPurpose),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = CreateToken(claims, now.UtcDateTime, expires.UtcDateTime);
        return new TwoFactorChallenge(token, expires);
    }

    public bool TryValidateTwoFactorChallenge(string token, out Guid userId, out Guid tenantId)
    {
        userId = Guid.Empty;
        tenantId = Guid.Empty;

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, ValidationParameters(), out _);

            if (!string.Equals(principal.FindFirst(AuthClaims.Purpose)?.Value, AuthClaims.TwoFactorPurpose, StringComparison.Ordinal))
            {
                return false;
            }

            return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId)
                && Guid.TryParse(principal.FindFirst(AuthClaims.TenantId)?.Value, out tenantId);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string CreateToken(IEnumerable<Claim> claims, DateTime notBefore, DateTime expires)
    {
        var credentials = new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _jwt.Issuer,
            _jwt.Audience,
            claims,
            notBefore,
            expires,
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(_jwt.SigningKey));

    private TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = _jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey(),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = AuthClaims.Subject,
        RoleClaimType = AuthClaims.Role,
    };
}
