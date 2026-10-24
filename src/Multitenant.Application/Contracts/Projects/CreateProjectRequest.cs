using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Projects;

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    ProjectStatus Status = ProjectStatus.Active);
