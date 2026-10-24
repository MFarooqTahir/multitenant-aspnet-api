using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "multitenant-aspnet-api";

    [Required]
    public string Audience { get; set; } = "multitenant-aspnet-api";

    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool SingleSessionPerUser { get; set; } = true;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    [Required]
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    [Range(1, 30)]
    public int TwoFactorChallengeMinutes { get; set; } = 5;
}

public sealed class IdentityFeatureOptions
{
    public const string SectionName = "Identity";

    public bool RequireConfirmedEmail { get; set; }

    public bool EnableTwoFactor { get; set; }
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Logging";

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@multitenant.local";

    [Required]
    public string FromName { get; set; } = "Multitenant API";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicyOptions Tenant { get; set; } = new();

    public RateLimitPolicyOptions Auth { get; set; } = new();
}

public sealed class RateLimitPolicyOptions
{
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrate { get; set; }

    public bool SeedDemoData { get; set; }
}

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public string Password { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
