using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration.Identity;
using Multitenant.Application.Configuration.Jwt;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Configures JWT bearer validation and turns a validated token into a live
/// authorization decision: the tenant, user, security stamp, role and (when
/// required) email confirmation are re-checked against the database on every
/// request, so a role change or "<c>logout everywhere</c>" invalidates already-issued
/// access tokens immediately.
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
            // Pin the algorithm: only HMAC-SHA256 tokens signed with this key are
            // accepted, so a token cannot ask the handler to switch algorithms.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
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
        var tenantSlug = principal?.FindFirst(AuthClaims.TenantSlug)?.Value;

        if (principal?.Identity is not ClaimsIdentity
            || !Guid.TryParse(principal.FindFirst(AuthClaims.Subject)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(AuthClaims.TenantId)?.Value, out var tenantId)
            || string.IsNullOrWhiteSpace(tenantSlug))
        {
            context.Fail("The access token is missing required claims.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var dbContext = services.GetRequiredService<AppDbContext>();
        var identityOptions = services.GetRequiredService<IOptions<IdentityFeatureOptions>>().Value;

        // One projection replaces the tenant, user and role reads. It still runs on
        // every request, preserving immediate revocation without a cache window.
        var state = await (
                from user in dbContext.Users.IgnoreQueryFilters()
                join tenant in dbContext.Tenants on user.TenantId equals tenant.Id
                where user.Id == userId && user.TenantId == tenantId
                select new
                {
                    tenant.IsActive,
                    tenant.Slug,
                    TokenSlugMatches = tenant.Slug == tenantSlug,
                    UserActive = user.IsActive,
                    EmailConfirmed = user.EmailConfirmed,
                    user.SecurityStamp,
                    Role = (
                        from userRole in dbContext.UserRoles
                        join role in dbContext.Roles on userRole.RoleId equals role.Id
                        where userRole.UserId == user.Id
                        select role.Name
                    ).FirstOrDefault(),
                })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (state is null || !state.IsActive || !state.TokenSlugMatches || !state.UserActive)
        {
            context.Fail("The tenant or user is no longer active.");
            return;
        }

        if (identityOptions.RequireConfirmedEmail && !state.EmailConfirmed)
        {
            context.Fail("Email confirmation is required.");
            return;
        }

        var tokenStamp = principal.FindFirst(AuthClaims.SecurityStamp)?.Value;

        // Fail closed: a missing stamp must never silently disable live invalidation.
        if (string.IsNullOrEmpty(tokenStamp)
            || string.IsNullOrEmpty(state.SecurityStamp)
            || !string.Equals(tokenStamp, state.SecurityStamp, StringComparison.Ordinal))
        {
            context.Fail("The security stamp is missing or has changed; acquire a new access token.");
            return;
        }

        var currentRole = state.Role ?? TenantRoles.Member;
        var tokenRole = principal.FindFirst(AuthClaims.Role)?.Value;

        if (!string.Equals(tokenRole, currentRole, StringComparison.Ordinal))
        {
            context.Fail("The user's role has changed; acquire a new access token.");
            return;
        }

        tenantContext.SetTenant(tenantId, state.Slug);
    }
}
