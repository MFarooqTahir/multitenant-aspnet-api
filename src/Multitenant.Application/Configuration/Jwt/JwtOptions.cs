using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration.Jwt;

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
