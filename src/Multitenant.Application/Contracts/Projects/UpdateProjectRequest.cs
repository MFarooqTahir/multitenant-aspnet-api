using Multitenant.Domain.Entities;

namespace Multitenant.Application.Contracts;

public sealed record UpdateProjectRequest(string Name, string? Description, ProjectStatus Status);
