namespace Multitenant.Application.Contracts;

public sealed class UserQuery : PageQuery
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }
}
