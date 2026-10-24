namespace Multitenant.Application.Abstractions;

public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
