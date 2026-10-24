using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Configuration;
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
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{_auth.PublicBaseUrl.TrimEnd('/')}/api/v1/auth/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        var message = new EmailMessage(
            user.Email ?? string.Empty,
            "Confirm your email address",
            $"<p>Confirm your email address to activate your account.</p><p><a href=\"{link}\">Confirm email</a></p>",
            $"Confirm your email address: {link}");

        try
        {
            await emailSender.SendAsync(message, cancellationToken);
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
