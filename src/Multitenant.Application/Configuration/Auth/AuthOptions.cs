using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When true, a new login or refresh rotates the security stamp and revokes the
    /// user's other refresh tokens, so only the newest session remains valid.
    /// </summary>
    public bool SingleSessionPerUser { get; set; } = true;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    [Range(1, 30)]
    public int TwoFactorChallengeMinutes { get; set; } = 5;

    /// <summary>
    /// True when the base URL cannot leak a confirmation token in clear text:
    /// HTTPS anywhere, or plain HTTP on a loopback host for local development.
    /// </summary>
    public bool UsesSecureBaseUrl() =>
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
