using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Multitenant.Application.Abstractions.Messaging;
using Multitenant.Application.Configuration.Email;

namespace Multitenant.Infrastructure.Email;

/// <summary>
/// SMTP sender backed by MailKit, used when <c>Email:Provider</c> is <c>Smtp</c>.
/// </summary>
public sealed partial class SmtpEmailSender(
    IOptions<EmailOptions> emailOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = emailOptions.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = _options.Smtp;

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            throw new InvalidOperationException(
                "Email:Provider is 'Smtp' but Email:Smtp:Host is not configured.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;

        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        }.ToMessageBody();

        using var client = new SmtpClient();

        if (!smtp.UseStartTls)
        {
            LogPlaintextWarning(logger, smtp.Host);
        }

        var secureSocketOptions = smtp.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(smtp.Host, smtp.Port, secureSocketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        LogSent(logger, message.ToAddress, message.Subject, smtp.Host);
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Sent email to {ToAddress} with subject {Subject} via {Host}.")]
    private static partial void LogSent(ILogger logger, string toAddress, string subject, string host);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "SMTP:UseStartTls is false; the connection to {Host} is unencrypted.")]
    private static partial void LogPlaintextWarning(ILogger logger, string host);
}
