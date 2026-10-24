using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts;

public sealed class ProjectQuery : PageQuery
{
    public string? Search { get; set; }

    public ProjectStatus? Status { get; set; }
}
