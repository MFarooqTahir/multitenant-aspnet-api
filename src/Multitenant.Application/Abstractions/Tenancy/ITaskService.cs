using Multitenant.Application.Contracts;

namespace Multitenant.Application.Abstractions;

/// <summary>
/// Task CRUD. Every method runs inside the caller's tenant; an id from another
/// tenant resolves to "not found".
/// </summary>
public interface ITaskService
{
    Task<PagedResult<TaskResponse>> ListAsync(TaskQuery query, CancellationToken cancellationToken);

    Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<TaskResponse> CreateAsync(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken);

    Task<TaskResponse> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
