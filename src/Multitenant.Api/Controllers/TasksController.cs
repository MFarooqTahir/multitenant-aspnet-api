using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Api.Authorization;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tasks;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = Policies.Member)]
[Route("api/v{version:apiVersion}")]
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
    /// <summary>Lists tasks across the tenant, with filters for project, status, priority and assignee.</summary>
    [HttpGet("tasks")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TaskResponse>>> List(
        [FromQuery] TaskQuery query,
        CancellationToken cancellationToken) =>
        Ok(await taskService.ListAsync(query, cancellationToken));

    /// <summary>Returns one task. An id from another tenant returns 404.</summary>
    [HttpGet("tasks/{id:guid}")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await taskService.GetAsync(id, cancellationToken));

    /// <summary>Lists the tasks of one project.</summary>
    [HttpGet("projects/{projectId:guid}/tasks")]
    [ProducesResponseType<PagedResult<TaskResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TaskResponse>>> ListForProject(
        Guid projectId,
        [FromQuery] TaskQuery query,
        CancellationToken cancellationToken)
    {
        query.ProjectId = projectId;
        return Ok(await taskService.ListAsync(query, cancellationToken));
    }

    /// <summary>Creates a task in a project. Any member of the tenant may create tasks.</summary>
    [HttpPost("projects/{projectId:guid}/tasks")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponse>> Create(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await taskService.CreateAsync(projectId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = task.Id, version = "1.0" }, task);
    }

    /// <summary>Updates a task.</summary>
    [HttpPut("tasks/{id:guid}")]
    [ProducesResponseType<TaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskResponse>> Update(
        Guid id,
        UpdateTaskRequest request,
        CancellationToken cancellationToken) =>
        Ok(await taskService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Deletes a task. Owner or Admin only.</summary>
    [HttpDelete("tasks/{id:guid}")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await taskService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
