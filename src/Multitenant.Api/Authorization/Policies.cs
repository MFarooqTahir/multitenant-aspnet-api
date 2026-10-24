using Microsoft.AspNetCore.Authorization;
using Multitenant.Domain.Entities;

namespace Multitenant.Api.Authorization;

/// <summary>
/// Named authorization policies. Roles are a coarse-grained shortcut; row-level
/// isolation is enforced by the tenant query filter and write guard, not here.
/// </summary>
public static class Policies
{
    /// <summary>Owner or Admin — may manage users and projects.</summary>
    public const string ManageTenant = "manage_tenant";

    /// <summary>Owner only — the highest-privilege operations.</summary>
    public const string OwnerOnly = "owner_only";

    /// <summary>Any authenticated role (Owner, Admin or Member).</summary>
    public const string Member = "member";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ManageTenant, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(TenantRoles.Owner, TenantRoles.Admin));

        options.AddPolicy(OwnerOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(TenantRoles.Owner));

        options.AddPolicy(Member, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(TenantRoles.All));
    }
}
