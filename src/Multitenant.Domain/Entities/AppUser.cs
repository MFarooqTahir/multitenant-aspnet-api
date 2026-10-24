using Microsoft.AspNetCore.Identity;

namespace Multitenant.Domain.Entities;

public sealed class AppUser : IdentityUser<Guid>, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
}
