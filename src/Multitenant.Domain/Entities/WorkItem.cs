namespace Multitenant.Domain.Entities;

public sealed class WorkItem : ITenantOwned, IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Todo;

    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;

    public Guid? AssigneeUserId { get; set; }

    public AppUser? Assignee { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
