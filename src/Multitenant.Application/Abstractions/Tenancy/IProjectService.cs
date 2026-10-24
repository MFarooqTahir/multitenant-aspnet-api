using Multitenant.Application.Contracts;

namespace Multitenant.Application.Abstractions;

/// <summary>
/// Project CRUD. Every method runs inside the caller's tenant.
/// </summary>
public interface IProjectService
{
    Task<PagedResult<ProjectResponse>> ListAsync(ProjectQuery query, CancellationToken cancellationToken);

    Task<ProjectResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
