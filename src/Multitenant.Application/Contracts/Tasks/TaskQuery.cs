using Multitenant.Application.Contracts.Common;
using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Tasks;

public sealed class TaskQuery : PageQuery
{
    public string? Search { get; set; }

    public WorkItemStatus? Status { get; set; }

    public WorkItemPriority? Priority { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? AssigneeUserId { get; set; }

    public DateOnly? DueBefore { get; set; }

    public DateOnly? DueAfter { get; set; }
}
