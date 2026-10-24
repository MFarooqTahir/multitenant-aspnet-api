using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Configuration;
using Multitenant.Application.Contracts;
using Multitenant.Application.Errors;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Identity;

/// <summary>
/// Implements the authentication lifecycle: password login, optional two-factor,
/// refresh-token rotation, logout, email confirmation and registration.
/// </summary>
/// <remarks>
/// Tenant-owned tables are protected by global query filters, so any lookup that
/// runs before a tenant is known uses <c>IgnoreQueryFilters()</c> together with an
/// explicit tenant predicate. The tenant context is then set before Identity writes.
/// </remarks>
public sealed class AuthService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ITenantContext tenantContext,
    IUserProvisioningService userProvisioning,
    ICurrentUser currentUser,
    IJwtTokenGenerator jwtTokenGenerator,
    RefreshTokenManager refreshTokenManager,
    IAccountEmailService accountEmailService,
    IOptions<AuthOptions> authOptions,
    IOptions<IdentityFeatureOptions> identityOptions,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAuthService
{
    private readonly AuthOptions _auth = authOptions.Value;
    private readonly IdentityFeatureOptions _identity = identityOptions.Value;
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var tenant = await FindTenantBySlugAsync(request.TenantSlug, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            throw new UnauthorizedException();
        }

        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var user = await FindUserByEmailAsync(tenant.Id, request.Email, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException();
        }

        if (_identity.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            throw new ForbiddenException(
                "Email confirmation is required before signing in.",
                "email_not_confirmed");
        }

        var password = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!password.Succeeded)
        {
            throw new UnauthorizedException();
        }

        if (_identity.EnableTwoFactor && user.TwoFactorEnabled)
        {
            var challenge = jwtTokenGenerator.CreateTwoFactorChallenge(user);
            return new LoginTwoFactorRequired(
                challenge.Value,
                SecondsUntil(challenge.ExpiresAtUtc));
        }

        var role = await GetRoleAsync(user);
        return new LoginSuccess(
            await IssueTokensAsync(user, role, tenant.Slug, ipAddress, cancellationToken));
    }

    public async Task<UserResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await FindTenantBySlugAsync(request.TenantSlug, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            throw new NotFoundException("Tenant not found.", "tenant_not_found");
        }

        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var user = await userProvisioning.CreateAsync(
            tenant.Id,
            tenant.Slug,
            request.Email,
            request.DisplayName,
            request.Password,
            TenantRoles.Member,
            cancellationToken);

        return userProvisioning.ToResponse(user, TenantRoles.Member);
    }

    public async Task<AuthTokens> RefreshAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TokenHash == RefreshTokenManager.Hash(refreshToken),
                cancellationToken);

        if (stored is null)
        {
            throw InvalidRefreshToken();
        }

        // Presenting an already-rotated token means the token was stolen or replayed.
        // Revoke the whole family so neither party can continue.
        if (stored.RevokedAtUtc is not null)
        {
            await refreshTokenManager.RevokeFamilyAsync(
                stored.FamilyId,
                "refresh_token_reuse",
                cancellationToken);

            throw InvalidRefreshToken();
        }

        var now = timeProvider.GetUtcNow();
        if (stored.ExpiresAtUtc <= now)
        {
            throw new UnauthorizedException("Refresh token has expired.", "refresh_token_expired");
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == stored.UserId && x.TenantId == stored.TenantId,
                cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw InvalidRefreshToken();
        }

        var tenant = await FindTenantByIdAsync(user.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            throw InvalidRefreshToken();
        }

        tenantContext.SetTenant(tenant.Id, tenant.Slug);
        var role = await GetRoleAsync(user);
        var replacementId = Guid.NewGuid();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Conditional update: only the request that wins the race can rotate this token.
        var rotated = await dbContext.RefreshTokens
            .Where(x => x.Id == stored.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.ReplacedByTokenId, replacementId)
                    .SetProperty(x => x.RevokedReason, "rotated"),
                cancellationToken);

        if (rotated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await refreshTokenManager.RevokeFamilyAsync(
                stored.FamilyId,
                "refresh_token_reuse",
                cancellationToken);

            throw InvalidRefreshToken();
        }

        if (_auth.SingleSessionPerUser)
        {
            await userManager.UpdateSecurityStampAsync(user);
            await refreshTokenManager.RevokeAllForUserAsync(user.Id, "single_session", cancellationToken);
        }

        var accessToken = jwtTokenGenerator.CreateAccessToken(user, role, tenant.Slug);
        var (rawToken, _) = refreshTokenManager.Create(user, ipAddress, stored.FamilyId, replacementId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AuthTokens(accessToken.Value, rawToken, SecondsUntil(accessToken.ExpiresAtUtc));
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var stored = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(
                x => x.TokenHash == RefreshTokenManager.Hash(refreshToken),
                cancellationToken);

        if (stored is null || stored.RevokedAtUtc is not null)
        {
            return;
        }

        stored.RevokedAtUtc = timeProvider.GetUtcNow();
        stored.RevokedReason = "logout";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAllAsync(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);

        await userManager.UpdateSecurityStampAsync(user);
        await refreshTokenManager.RevokeAllForUserAsync(user.Id, "logout_all", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.RequireConfirmedEmail);

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw InvalidConfirmationToken();

        var tenant = await FindTenantByIdAsync(user.TenantId, cancellationToken)
            ?? throw InvalidConfirmationToken();

        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw InvalidConfirmationToken();
        }
    }

    public async Task ResendConfirmationAsync(
        string tenantSlug,
        string email,
        CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.RequireConfirmedEmail);

        var tenant = await FindTenantBySlugAsync(tenantSlug, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            return;
        }

        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        var user = await FindUserByEmailAsync(tenant.Id, email, cancellationToken);
        if (user is null || user.EmailConfirmed || !user.IsActive)
        {
            return;
        }

        await accountEmailService.SendConfirmationEmailAsync(user, cancellationToken);
    }

    public async Task<TwoFactorSetupResponse> SetupTwoFactorAsync(CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.EnableTwoFactor);
        var user = await GetCurrentUserAsync(cancellationToken);

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Could not create an authenticator key.");
        }

        var issuer = Uri.EscapeDataString(_jwt.Issuer);
        var account = Uri.EscapeDataString(user.Email ?? user.Id.ToString());
        var authenticatorUri = $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";

        return new TwoFactorSetupResponse(key, authenticatorUri);
    }

    public async Task<TwoFactorEnabledResponse> EnableTwoFactorAsync(
        string code,
        CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.EnableTwoFactor);
        var user = await GetCurrentUserAsync(cancellationToken);
        await VerifyAuthenticatorCodeAsync(user, code);

        user.TwoFactorEnabled = true;
        await userManager.UpdateAsync(user);

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? [];
        return new TwoFactorEnabledResponse(recoveryCodes.ToList());
    }

    public async Task DisableTwoFactorAsync(string code, CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.EnableTwoFactor);
        var user = await GetCurrentUserAsync(cancellationToken);
        await VerifyAuthenticatorCodeAsync(user, code);

        user.TwoFactorEnabled = false;
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.UpdateAsync(user);
    }

    public async Task<AuthTokens> VerifyTwoFactorAsync(
        TwoFactorVerifyRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        EnsureFeature(_identity.EnableTwoFactor);

        if (!jwtTokenGenerator.TryValidateTwoFactorChallenge(
                request.TwoFactorToken,
                out var userId,
                out var tenantId))
        {
            throw InvalidTwoFactorChallenge();
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == userId && x.TenantId == tenantId,
                cancellationToken);

        if (user is null || !user.IsActive || !user.TwoFactorEnabled)
        {
            throw InvalidTwoFactorChallenge();
        }

        var tenant = await FindTenantByIdAsync(user.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            throw InvalidTwoFactorChallenge();
        }

        tenantContext.SetTenant(tenant.Id, tenant.Slug);

        if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            var recovery = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode);
            if (!recovery.Succeeded)
            {
                throw new UnauthorizedException("Invalid recovery code.", "invalid_two_factor_code");
            }
        }
        else
        {
            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                request.Code ?? string.Empty);

            if (!valid)
            {
                await userManager.AccessFailedAsync(user);
                throw new UnauthorizedException("Invalid authenticator code.", "invalid_two_factor_code");
            }
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var role = await GetRoleAsync(user);

        return await IssueTokensAsync(user, role, tenant.Slug, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Issues a fresh access/refresh pair. With single-session enabled the security
    /// stamp is rotated first, which invalidates every access token issued earlier.
    /// </summary>
    private async Task<AuthTokens> IssueTokensAsync(
        AppUser user,
        string role,
        string tenantSlug,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (_auth.SingleSessionPerUser)
        {
            var stamp = await userManager.UpdateSecurityStampAsync(user);
            if (!stamp.Succeeded)
            {
                throw new InvalidOperationException("Could not update the user security stamp.");
            }

            await refreshTokenManager.RevokeAllForUserAsync(user.Id, "single_session", cancellationToken);
        }

        var accessToken = jwtTokenGenerator.CreateAccessToken(user, role, tenantSlug);
        var (rawToken, _) = refreshTokenManager.Create(user, ipAddress);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokens(accessToken.Value, rawToken, SecondsUntil(accessToken.ExpiresAtUtc));
    }

    private async Task<AppUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.Id == currentUser.UserId && x.TenantId == tenantId,
                cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException();
        }

        return user;
    }

    private Task<Tenant?> FindTenantBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = TenantSlug.Normalize(slug);
        return dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalized, cancellationToken);
    }

    private Task<Tenant?> FindTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);

    private Task<AppUser?> FindUserByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();

        return dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    private async Task<string> GetRoleAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? TenantRoles.Member;
    }

    private async Task VerifyAuthenticatorCodeAsync(AppUser user, string code)
    {
        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!valid)
        {
            throw new UnauthorizedException("Invalid authenticator code.", "invalid_two_factor_code");
        }
    }

    private int SecondsUntil(DateTimeOffset moment) =>
        Math.Max(1, (int)(moment - timeProvider.GetUtcNow()).TotalSeconds);

    private static void EnsureFeature(bool enabled)
    {
        if (!enabled)
        {
            throw new FeatureDisabledException();
        }
    }

    private static UnauthorizedException InvalidRefreshToken() =>
        new("Invalid refresh token.", "invalid_refresh_token");

    private static UnauthorizedException InvalidTwoFactorChallenge() =>
        new("Invalid two-factor challenge.", "invalid_two_factor_challenge");

    private static ConflictException InvalidConfirmationToken() =>
        new("Invalid or expired confirmation token.", "invalid_confirmation_token");
}
