using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts.Projects;

public sealed record UpdateProjectRequest(string Name, string? Description, ProjectStatus Status);
