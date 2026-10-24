using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Api.Authorization;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Authorize(Policy = Policies.Member)]
[Route("api/v{version:apiVersion}/projects")]
public sealed class ProjectsController(IProjectService projectService) : ControllerBase
{
    /// <summary>Lists projects of the current tenant.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectResponse>>> List(
        [FromQuery] ProjectQuery query,
        CancellationToken cancellationToken) =>
        Ok(await projectService.ListAsync(query, cancellationToken));

    /// <summary>Returns one project. An id from another tenant returns 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await projectService.GetAsync(id, cancellationToken));

    /// <summary>Creates a project. Owner or Admin only.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectResponse>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = project.Id, version = "1.0" }, project);
    }

    /// <summary>Updates a project. Owner or Admin only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Update(
        Guid id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken) =>
        Ok(await projectService.UpdateAsync(id, request, cancellationToken));

    /// <summary>Deletes a project and its tasks. Owner or Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await projectService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
