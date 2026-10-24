using Microsoft.AspNetCore.Identity;

namespace Multitenant.Domain.Entities;

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string roleName)
        : base(roleName)
    {
    }
}
