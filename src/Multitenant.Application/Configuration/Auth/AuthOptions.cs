using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration;

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
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    [Range(1, 30)]
    public int TwoFactorChallengeMinutes { get; set; } = 5;
}
