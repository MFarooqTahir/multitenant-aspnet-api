using Multitenant.Domain.Entities;

namespace Multitenant.Application.Common;

public static class UserNames
{
    /// <summary>
    /// Identity requires a unique <c>UserName</c> per store, but the same email may
    /// belong to users of different tenants. Namespacing the user name with the
    /// tenant slug keeps emails unique only within a tenant (see the composite
    /// unique index on <c>TenantId + NormalizedEmail</c>).
    /// </summary>
    public static string For(string tenantSlug, string email) =>
        $"{TenantSlug.Normalize(tenantSlug)}:{email.Trim().ToLowerInvariant()}";
}
