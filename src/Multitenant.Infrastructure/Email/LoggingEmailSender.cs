using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
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
        var safeBody = TokenPattern().Replace(message.TextBody ?? string.Empty, "$1token=[REDACTED]");
        LogEmail(logger, message.ToAddress, message.Subject, safeBody);
        return Task.CompletedTask;
    }

    // Redacts email confirmation tokens that appear as query-string or body values,
    // so they are not persisted to log aggregation systems in plaintext.
    [GeneratedRegex(@"(\?|&)token=[^&\s""]+|token=[^&\s""]+")]
    private static partial Regex TokenPattern();

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Email to {ToAddress} with subject {Subject}. Body: {TextBody}")]
    private static partial void LogEmail(ILogger logger, string toAddress, string subject, string textBody);
}
