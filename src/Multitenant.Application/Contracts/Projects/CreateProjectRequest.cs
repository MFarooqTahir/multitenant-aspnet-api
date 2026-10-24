using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    ProjectStatus Status = ProjectStatus.Active);
