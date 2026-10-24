using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Multitenant.Api.Authorization;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
public sealed class TenantsController(ITenantService tenantService) : ControllerBase
{
    /// <summary>
    /// Provisions a new tenant (roles, owner account and starter data) in one transaction.
    /// Anonymous by design: this is the self-service sign-up entry point.
    /// </summary>
    [HttpPost("onboard")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TenantResponse>> Onboard(
        OnboardTenantRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantService.OnboardAsync(request, cancellationToken);
        return Created($"/api/v1/tenants/{tenant.Id}", tenant);
    }

    /// <summary>Returns the tenant resolved from the access token.</summary>
    [HttpGet("current")]
    [Authorize(Policy = Policies.Member)]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> Current(CancellationToken cancellationToken) =>
        Ok(await tenantService.GetCurrentAsync(cancellationToken));
}
