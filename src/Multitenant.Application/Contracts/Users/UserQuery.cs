using Multitenant.Application.Contracts.Common;

namespace Multitenant.Application.Contracts.Users;

public sealed class UserQuery : PageQuery
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }
}
