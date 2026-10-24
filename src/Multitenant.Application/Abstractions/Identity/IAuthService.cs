using Multitenant.Application.Contracts;

namespace Multitenant.Application.Abstractions;

/// <summary>
/// Login, registration, session and account-security operations.
/// </summary>
public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthTokens> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    Task LogoutAllAsync(CancellationToken cancellationToken);

    Task ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    Task ResendConfirmationAsync(string tenantSlug, string email, CancellationToken cancellationToken);

    Task<TwoFactorSetupResponse> SetupTwoFactorAsync(CancellationToken cancellationToken);

    Task<TwoFactorEnabledResponse> EnableTwoFactorAsync(string code, CancellationToken cancellationToken);

    Task DisableTwoFactorAsync(string code, CancellationToken cancellationToken);

    Task<AuthTokens> VerifyTwoFactorAsync(
        TwoFactorVerifyRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);
}
