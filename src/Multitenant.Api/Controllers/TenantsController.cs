using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Multitenant.Api.Authorization;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Contracts.Tenants;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tenants")]
public sealed class TenantsController(
    ITenantService tenantService,
    ITenantFeatureService tenantFeatureService) : ControllerBase
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

    /// <summary>
    /// Renames the current tenant. The slug is immutable because it is the login key;
    /// renaming the tenant never changes how users sign in.
    /// </summary>
    [HttpPut("current")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> UpdateCurrent(
        UpdateTenantRequest request,
        CancellationToken cancellationToken) =>
        Ok(await tenantService.UpdateCurrentAsync(request, cancellationToken));

    /// <summary>Lists every feature switch and its effective value for this tenant.</summary>
    [HttpGet("current/features")]
    [Authorize(Policy = Policies.Member)]
    [ProducesResponseType<IReadOnlyList<TenantFeatureResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantFeatureResponse>>> GetFeatures(
        CancellationToken cancellationToken) =>
        Ok(await tenantFeatureService.ListAsync(cancellationToken));

    /// <summary>
    /// Turns one whitelisted feature on or off for this tenant. Owner only; an unknown
    /// key is a 404 because it names a capability that does not exist.
    /// </summary>
    [HttpPut("current/features/{key}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType<TenantFeatureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantFeatureResponse>> UpdateFeature(
        string key,
        UpdateTenantFeatureRequest request,
        CancellationToken cancellationToken) =>
        Ok(await tenantFeatureService.SetAsync(key, request.IsEnabled, cancellationToken));

    /// <summary>
    /// Returns a tenant by id. Only the caller's own tenant is reachable, so the
    /// <c>Location</c> header returned by onboarding resolves instead of 404ing.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.Member)]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await tenantService.GetCurrentAsync(cancellationToken);

        if (tenant.Id != id)
        {
            return NotFound();
        }

        return Ok(tenant);
    }
}
