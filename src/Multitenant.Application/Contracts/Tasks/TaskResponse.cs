using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Tasks;

public sealed record TaskResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    Guid? AssigneeUserId,
    DateOnly? DueDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
