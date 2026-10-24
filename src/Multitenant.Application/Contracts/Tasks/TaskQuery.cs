using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts;

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
