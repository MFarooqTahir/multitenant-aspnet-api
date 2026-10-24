using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>Logging</c> or <c>Smtp</c>.</summary>
    public string Provider { get; set; } = "Logging";

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@multitenant.local";

    [Required]
    public string FromName { get; set; } = "Multitenant API";

    public SmtpOptions Smtp { get; set; } = new();
}
