using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Messaging;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Domain.Entities;

namespace Multitenant.Infrastructure.Identity;

public sealed partial class AccountEmailService(
    UserManager<AppUser> userManager,
    IEmailSender emailSender,
    IOptions<AuthOptions> authOptions,
    ILogger<AccountEmailService> logger) : IAccountEmailService
{
    private readonly AuthOptions _auth = authOptions.Value;

    public async Task SendConfirmationEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        try
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

            // The token is placed in the URL fragment, which browsers do not send to the
            // server or include in Referer headers. The confirmation page removes the
            // fragment and POSTs the token in the request body.
            var link = $"{_auth.PublicBaseUrl.TrimEnd('/')}/confirm-email#userId={user.Id}&token={Uri.EscapeDataString(token)}";
            var message = new EmailMessage(
                user.Email ?? string.Empty,
                "Confirm your email address",
                $"<p>Confirm your email address to activate your account.</p><p><a href=\"{link.Replace("&", "&amp;", StringComparison.Ordinal)}\">Confirm email</a></p>",
                $"Confirm your email address: {link}");

            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSendFailed(logger, ex, user.Id);
        }
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Could not send a confirmation email for user {UserId}.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid userId);
}
