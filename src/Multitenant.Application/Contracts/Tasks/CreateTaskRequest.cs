using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Tasks;

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    WorkItemStatus Status = WorkItemStatus.Todo,
    WorkItemPriority Priority = WorkItemPriority.Medium,
    Guid? AssigneeUserId = null,
    DateOnly? DueDate = null);
