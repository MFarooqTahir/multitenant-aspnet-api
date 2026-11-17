using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tasks;

namespace Multitenant.Application.Abstractions.Tenancy;

/// <summary>
/// Task CRUD. Every method runs inside the caller's tenant; an id from another
/// tenant resolves to "not found".
/// </summary>
public interface ITaskService
{
    Task<PagedResult<TaskResponse>> ListAsync(TaskQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the tasks of one project. Unknown and cross-tenant project ids return
    /// not-found, matching every other tenant-scoped resource read.
    /// </summary>
    Task<PagedResult<TaskResponse>> ListForProjectAsync(
        Guid projectId,
        TaskQuery query,
        CancellationToken cancellationToken);

    Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<TaskResponse> CreateAsync(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken);

    Task<TaskResponse> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
