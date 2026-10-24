using Microsoft.AspNetCore.Http;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Common;
using Multitenant.Application.Errors;

namespace Multitenant.Infrastructure.Identity;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated is true;

    public Guid UserId => Guid.TryParse(Claim(AuthClaims.Subject), out var id)
        ? id
        : throw new UnauthorizedException();

    public Guid TenantId => Guid.TryParse(Claim(AuthClaims.TenantId), out var id)
        ? id
        : throw new UnauthorizedException();

    public string Role => Claim(AuthClaims.Role) ?? throw new UnauthorizedException();

    private string? Claim(string type) => httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
}
