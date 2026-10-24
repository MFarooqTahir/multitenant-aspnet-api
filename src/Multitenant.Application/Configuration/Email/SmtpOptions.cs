using System.ComponentModel.DataAnnotations;

namespace Multitenant.Application.Configuration.Email;

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
