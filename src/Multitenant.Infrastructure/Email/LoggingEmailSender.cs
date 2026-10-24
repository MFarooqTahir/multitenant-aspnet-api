using Microsoft.Extensions.Logging;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Abstractions.Messaging;

namespace Multitenant.Infrastructure.Email;

/// <summary>
/// Development/no-op sender: logs the message instead of delivering it. Used when
/// <c>Email:Provider</c> is <c>Logging</c>, which is the default outside production.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogEmail(logger, message.ToAddress, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Email to {ToAddress} with subject {Subject}. Body: {TextBody}")]
    private static partial void LogEmail(ILogger logger, string toAddress, string subject, string textBody);
}
