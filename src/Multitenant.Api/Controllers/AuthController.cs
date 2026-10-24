using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Multitenant.Api.RateLimiting;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Results;
using Multitenant.Application.Contracts.Users;

namespace Multitenant.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Signs in and returns an access token plus a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, ClientIp(), cancellationToken);

        return result switch
        {
            LoginSuccess success => Ok(success.Tokens),
            LoginTwoFactorRequired challenge => Ok(new
            {
                twoFactorRequired = true,
                challenge.TwoFactorToken,
                challenge.ExpiresIn,
            }),
            _ => throw new InvalidOperationException("Unknown login result."),
        };
    }

    /// <summary>
    /// Self-service registration into an existing tenant. New users always start as
    /// <c>Member</c>; an Owner or Admin promotes them through the users endpoints.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(
            StatusCodes.Status201Created,
            await authService.RegisterAsync(request, cancellationToken));

    /// <summary>Exchanges a refresh token for a new token pair (rotation + reuse detection).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokens>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.RefreshAsync(request.RefreshToken, ClientIp(), cancellationToken));

    /// <summary>Revokes the supplied refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>Revokes every refresh token and invalidates every issued access token.</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await authService.LogoutAllAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Completes email confirmation using the token from the confirmation email.
    /// The token is sent in the request body (not the query string) so it is not
    /// captured by proxy logs, browser history or Referer headers.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmail(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ConfirmEmailAsync(request.UserId, request.Token, cancellationToken);
        return NoContent();
    }

    /// <summary>Re-sends the confirmation email. Always returns 202 to avoid account enumeration.</summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendConfirmation(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ResendConfirmationAsync(request.TenantSlug, request.Email, cancellationToken);
        return Accepted();
    }

    /// <summary>Starts authenticator-app setup; returns the shared key and otpauth URI.</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    [ProducesResponseType<TwoFactorSetupResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(CancellationToken cancellationToken) =>
        Ok(await authService.SetupTwoFactorAsync(cancellationToken));

    /// <summary>Confirms the first authenticator code, enables 2FA and returns recovery codes.</summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    [ProducesResponseType<TwoFactorEnabledResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TwoFactorEnabledResponse>> EnableTwoFactor(
        TwoFactorEnableRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.EnableTwoFactorAsync(request.Code, cancellationToken));

    /// <summary>Disables 2FA after verifying the current authenticator code.</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisableTwoFactor(
        TwoFactorDisableRequest request,
        CancellationToken cancellationToken)
    {
        await authService.DisableTwoFactorAsync(request.Code, cancellationToken);
        return NoContent();
    }

    /// <summary>Completes a two-factor login challenge with an authenticator or recovery code.</summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.AuthPolicy)]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokens>> VerifyTwoFactor(
        TwoFactorVerifyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.VerifyTwoFactorAsync(request, ClientIp(), cancellationToken));

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
