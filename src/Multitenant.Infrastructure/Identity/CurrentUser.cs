using Microsoft.AspNetCore.Http;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common;
using Multitenant.Application.Errors;
using Multitenant.Application.Errors.Exceptions;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Reads the caller's identity from the validated JWT. Tenant identity is exposed
/// through <see cref="ITenantContext"/>, not here, so there is one place to look.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId => Guid.TryParse(Claim(AuthClaims.Subject), out var id)
        ? id
        : throw new UnauthorizedException();

    public string Role => Claim(AuthClaims.Role) ?? throw new UnauthorizedException();

    private string? Claim(string type) => httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
}
