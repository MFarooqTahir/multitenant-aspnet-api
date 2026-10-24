namespace Multitenant.Application.Abstractions;

/// <summary>
/// Delivers an email. Implementations are selected by <c>Email:Provider</c>
/// (<c>Logging</c> for development, <c>Smtp</c> for production).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
