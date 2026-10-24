using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Api.Authorization;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Contracts;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/users")]
public sealed class UsersController(IUserService userService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Returns the profile of the authenticated user.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Member)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken) =>
        Ok(await userService.GetAsync(currentUser.UserId, cancellationToken));

    /// <summary>Lists the users of the current tenant.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<PagedResult<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserResponse>>> List(
        [FromQuery] UserQuery query,
        CancellationToken cancellationToken) =>
        Ok(await userService.ListAsync(query, cancellationToken));

    /// <summary>Returns one user from the current tenant. Cross-tenant ids return 404.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await userService.GetAsync(id, cancellationToken));

    /// <summary>Creates a user in the current tenant and assigns a role.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id, version = "1.0" }, user);
    }

    /// <summary>Changes a user's role. Issued access tokens are invalidated immediately.</summary>
    [HttpPatch("{id:guid}/role")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> UpdateRole(
        Guid id,
        UpdateUserRoleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await userService.UpdateRoleAsync(id, request.Role, cancellationToken));

    /// <summary>Removes a user from the current tenant.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageTenant)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await userService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
