using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Tasks;

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate);
