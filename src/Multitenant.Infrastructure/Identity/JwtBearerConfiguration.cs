using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration;
using Multitenant.Application.Configuration.Jwt;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Configures JWT bearer validation and turns a validated token into a live
/// authorization decision: the security stamp and role are re-checked against the
/// database on every request, so a role change or "<c>logout everywhere</c>"
/// invalidates already-issued access tokens immediately.
/// </summary>
public static class JwtBearerConfiguration
{
    public static void Configure(JwtBearerOptions options, JwtOptions jwt)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthClaims.Subject,
            RoleClaimType = AuthClaims.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidateLiveUserAsync,
        };
    }

    private static async Task ValidateLiveUserAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;

        if (principal?.Identity is not ClaimsIdentity
            || !Guid.TryParse(principal.FindFirst(AuthClaims.Subject)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(AuthClaims.TenantId)?.Value, out var tenantId))
        {
            context.Fail("The access token is missing required claims.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var dbContext = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        tenantContext.SetTenant(tenantId, principal.FindFirst(AuthClaims.TenantSlug)?.Value ?? string.Empty);

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == tenantId,
                context.HttpContext.RequestAborted);

        if (tenant is null || !tenant.IsActive)
        {
            context.Fail("The tenant is not active.");
            return;
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == userId && x.TenantId == tenantId,
                context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive)
        {
            context.Fail("The user is no longer active.");
            return;
        }

        var tokenStamp = principal.FindFirst(AuthClaims.SecurityStamp)?.Value;
        if (!string.Equals(tokenStamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            // Covers role changes, logout-everywhere, password resets and single-session logins.
            context.Fail("The security stamp has changed; acquire a new access token.");
            return;
        }

        var roles = await userManager.GetRolesAsync(user);
        var currentRole = roles.FirstOrDefault() ?? TenantRoles.Member;
        var tokenRole = principal.FindFirst(AuthClaims.Role)?.Value;

        if (!string.Equals(tokenRole, currentRole, StringComparison.Ordinal))
        {
            context.Fail("The user's role has changed; acquire a new access token.");
        }
    }
}
