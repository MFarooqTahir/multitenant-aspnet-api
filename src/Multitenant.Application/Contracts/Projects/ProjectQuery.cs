using Multitenant.Application.Contracts.Common;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Projects;

public sealed class ProjectQuery : PageQuery
{
    public string? Search { get; set; }

    public ProjectStatus? Status { get; set; }
}
